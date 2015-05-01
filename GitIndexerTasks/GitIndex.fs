
(*
 * All rights reserved. This program and the accompanying materials
 * are made available under the terms of the GNU Lesser General Public License
 * (LGPL) version 2.1 which accompanies this distribution, and is available at
 * http://www.gnu.org/licenses/lgpl-2.1.html
 * 
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
 * Lesser General Public License for more details.
 *)

namespace GitIndexerTasks

open System
open System.IO
open System.Diagnostics
open System.Text.RegularExpressions
open LibGit2Sharp

open Microsoft.Build.Framework
open Microsoft.Build.Utilities

type GitIndex() =
    inherit Task()

    let relativePath file =
        let myFile = Reflection.Assembly.GetExecutingAssembly().Location
        let myPath = FileInfo(myFile).DirectoryName
        Path.Combine(myPath, file)

    let execute fileName arguments =
        let psi = ProcessStartInfo(fileName, arguments)
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true

        let proc = Process.Start(psi)

        proc.WaitForExit()

    let executeRead fileName arguments =
        let psi = ProcessStartInfo(fileName, arguments)
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        psi.RedirectStandardOutput <- true

        let proc = Process.Start(psi)

        let line = ref null

        seq {
            line := proc.StandardOutput.ReadLine()
            while !line <> null do
                yield !line
                line := proc.StandardOutput.ReadLine()
        }

    let findRepository file =
        let file = FileInfo(file)

        let directories = seq {
            let directory = ref file.Directory
            while (!directory).Name <> (!directory).FullName do
                yield !directory
                directory := (!directory).Parent
        }

        let existsGitDirectory (directory: DirectoryInfo) =
            let gitPath = Path.Combine(directory.FullName, ".git")
            Directory.Exists(gitPath)

        match Seq.tryFind existsGitDirectory directories with
            | Some(info) -> Some(info.FullName)
            | None -> None


    member val DbgToolsPath = Unchecked.defaultof<string> with get, set

    member val UseUrlSuffix = true with get, set

    member val UrlSuffix = Unchecked.defaultof<string> with get, set

    member private this.PdbstrPath =
        if String.IsNullOrWhiteSpace(this.DbgToolsPath) then
            relativePath @"dbgtools\pdbstr.exe"
        else
            Path.Combine(this.DbgToolsPath, "pdbstr.exe")

    member private this.ScrtoolPath =
        if String.IsNullOrWhiteSpace(this.DbgToolsPath) then
            relativePath @"dbgtools\srctool.exe"
        else
            Path.Combine(this.DbgToolsPath, "srctool.exe")

    member private this.MakeUrlSuffix(baseUrl: string) =
        let mutable url = (baseUrl.Trim())

        if url.EndsWith(".git", StringComparison.InvariantCultureIgnoreCase) then
            url <- url.Substring(0, url.Length - 4)
        else if url.EndsWith(".git/", StringComparison.InvariantCultureIgnoreCase) then
            url <- url.Substring(0, url.Length - 5)

        if not (url.EndsWith("/")) then
            url <- url + "/"

        if this.UseUrlSuffix then
            if String.IsNullOrWhiteSpace(this.UrlSuffix) then
                url <- url + "raw"
            else
                url <- url + this.UrlSuffix

            if not (url.EndsWith("/")) then
                url <- url + "/"

        url

    member private this.MakeSrcsrv pdb = 

        let readPdb pdb =
            let arguments = sprintf "-r %s" pdb
            executeRead this.ScrtoolPath arguments

        let infoFiles =
            readPdb pdb
            |> Seq.toList
            |> Seq.groupBy findRepository
            |> Seq.map (fun (repo, files) -> 
                match repo with
                    | Some(path) -> Some(new Repository(path)), files
                    | _ -> None, files
            )
            |> Seq.toList

        let serverNameUrl (repo: Repository) =
            let url = repo.Network.Remotes.["origin"].Url
            "_" + (Regex.Replace(url, "[^\w]", "_")).ToUpper(), this.MakeUrlSuffix(url)

        let indexed = ref false

        let srcsrv =
            seq {

                yield "SRCSRV: ini ------------------------------------------------"
                yield "VERSION=3"
                yield "INDEXVERSION=2"
                yield "VERCTRL=GIT"
                yield (sprintf "DATETIME=%s" (DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.fff")))
                yield "SRCSRV: variables ------------------------------------------"
                //yield "GIT_EXTRACT_CMD=%WINDIR%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe -Command \"& { (new-object System.Net.WebClient).DownloadFile('%fnvar%(%var2%)%var4%/%var3%', $args[0]) } %srcsrvtrg% \" "
                yield "GIT_EXTRACT_CMD=%WINDIR%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe -Command \"& { [System.IO.File]::WriteAllText($args[0], (new-object System.Net.WebClient).DownloadString('%fnvar%(%var2%)%var4%/%var3%').Replace([string][char]10, ([string][char]13) + ([string][char]10)).Replace(([string][char]13) + ([string][char]13) + ([string][char]10), ([string][char]13) + ([string][char]10)), [System.Text.Encoding]::GetEncoding(%var5%) ) } %srcsrvtrg% \" "
                yield "GIT_EXTRACT_TARGET=%targ%\%var4%\%fnbksl%(%var3%)"
                yield "SRCSRVVERCTRL=git"
                yield "SRCSRVERRDESC=access"
                yield "SRCSRVERRVAR=var2"

                let infoFilesInTfs =
                    infoFiles
                    |> Seq.where( fun (repo, files) -> repo.IsSome)
                    |> Seq.map ( fun (someRepo, files) -> someRepo.Value, files )

                for someRepo, files in infoFiles do
                    match someRepo with
                        | None ->
                            for file in files do
                                this.Log.LogWarning("Could not find Git repository for {0}", file)
                        | Some(repo) ->
                            let name, url = serverNameUrl repo
                            yield sprintf "%s=%s" name url

                yield "SRCSRVTRG=%GIT_extract_target%"
                yield "SRCSRVCMD=%GIT_extract_cmd%"
                yield "SRCSRV: source files ---------------------------------------"

                for repo, files in infoFilesInTfs do

                    let serverName,_ = serverNameUrl repo

                    let repoUri = Uri(repo.Info.WorkingDirectory)

                    let relativePath path =
                        repoUri.MakeRelativeUri(Uri(path)).ToString()

                    let filesRelatives =
                        files
                        |> Seq.map (fun file -> file, relativePath file)

                    let diff =
                        let diff = 
                            seq {
                                for diff in repo.Diff.Compare<TreeChanges>(Seq.map snd filesRelatives, true) do
                                    yield diff.Path.ToLower().Replace('\\', '/'), diff
                            }
                        Map(diff)

                    let commit = (Seq.head repo.Commits).Id.ToString()

                    for path, relative in filesRelatives do
                        let codePage = (
                            use stream = new System.IO.StreamReader(path, true)
                            stream.CurrentEncoding.CodePage
                        )

                        if diff.ContainsKey(relative.ToLower()) then
                            this.Log.LogWarning("File {0} is not in this repository.", path)
                        else
                            indexed := true
                            yield sprintf "%s*%s*%s*%s*%d" path serverName relative commit codePage


                yield "SRCSRV: end ------------------------------------------------"
            }
            |> Seq.toList

        srcsrv,!indexed

    member private this.WriteSrcsrv pdb =
        let tempFile = Path.Combine((Environment.GetEnvironmentVariable("TEMP")), (Guid.NewGuid().ToString() + ".txt"))

        match this.MakeSrcsrv pdb with
            | _,false -> false
            | srcsrv,true ->
                File.WriteAllLines(tempFile, srcsrv)

                try
                    let arguments = (sprintf "-w -p:%s -s:srcsrv -i:%s" pdb tempFile)
                    execute this.PdbstrPath arguments
                finally
                    File.Delete(tempFile)
                true


    [<RequiredAttribute>]
    member val PdbFiles = Unchecked.defaultof<string> with get, set

    override this.Execute() =

        for pdbFile in this.PdbFiles.Split([|';'|]) do
            this.Log.LogMessage("Indexing file {0}...", pdbFile)

            if this.WriteSrcsrv pdbFile then
                this.Log.LogMessage("{0} indexed.", pdbFile)
            else
                this.Log.LogWarning("{0} not indexed.", pdbFile)
        true
