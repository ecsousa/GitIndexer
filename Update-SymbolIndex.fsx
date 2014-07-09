
#r @".\packages\LibGit2Sharp.0.18.1.0\lib\net40\LibGit2Sharp.dll"

open System
open System.IO
open System.Diagnostics
open System.Text.RegularExpressions
open LibGit2Sharp

let relativePath file =
#if INTERACTIVE    
    let myFile = fsi.CommandLineArgs.[0]
#else
    let myFile = Reflection.Assembly.GetEntryAssembly().Location
#endif
    let myPath = FileInfo(myFile).DirectoryName
    Path.Combine(myPath, file)

let pdbstrPath = relativePath @"dbgtools\pdbstr.exe"
let srctoolPath = relativePath @"dbgtools\srctool.exe"

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

let replaceUrl (url: string) =
    let uri =
        if url.EndsWith("/") then
            Uri(url)
        elif url.EndsWith(".git") then
            Uri(Regex.Replace(url, "\\.git$", "/"))
        else
            Uri(url + "/")

    if uri.Host = "bitbucket.org" then
        Uri(uri, "raw/").ToString().ToLower()
    elif uri.Host = "github.com" then
        let gitRaw = Uri("https://raw.githubusercontent.com/")
        Uri(gitRaw, uri.AbsolutePath).ToString()
    else
        uri.ToString()


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



let makeSrcsrv pdb = 
    let readPdb pdb =
        let arguments = sprintf "-r %s" pdb
        executeRead srctoolPath arguments

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
        "_" + (Regex.Replace(url, "[^\w]", "_")).ToUpper(), replaceUrl url

    let indexed = ref false

    let srcsrv =
        seq {

            yield "SRCSRV: ini ------------------------------------------------"
            yield "VERSION=3"
            yield "INDEXVERSION=2"
            yield "VERCTRL=GIT"
            yield (sprintf "DATETIME=%s" (DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.fff")))
            yield "SRCSRV: variables ------------------------------------------"
            yield "GIT_EXTRACT_CMD=%WINDIR%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe -Command \"Invoke-WebRequest '%fnvar%(%var2%)%var4%/%var3%' -OutFile %srcsrvtrg% \" "
            yield "GIT_EXTRACT_TARGET=%targ%\%var2%\%fnbksl%(%var3%)\%var4%\%fnfile%(%var1%)"
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
                            printfn " - Não foi possível localizar repositório Git para %s." file
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
                    if diff.ContainsKey(relative.ToLower()) then
                        printfn " - Arquivo %s não está atualizado no repositório." path
                    else
                        indexed := true
                        yield sprintf "%s*%s*%s*%s" path serverName relative commit


            yield "SRCSRV: end ------------------------------------------------"
        }
        |> Seq.toList

    srcsrv,!indexed

let writeSrcsrv pdb =
    let tempFile = Path.Combine((Environment.GetEnvironmentVariable("TEMP")), (Guid.NewGuid().ToString() + ".txt"))

    match makeSrcsrv pdb with
        | _,false -> false
        | srcsrv,true ->
            File.WriteAllLines(tempFile, srcsrv)

            try
                let arguments = (sprintf "-w -p:%s -s:srcsrv -i:%s" pdb tempFile)
                execute pdbstrPath arguments
            finally
                File.Delete(tempFile)
            true


let regPattern = Regex(@"^(.*\\)?([^\\]+)$")

#if COMPILED
[<EntryPoint>]
#endif
let main(args: string[]) =  
    if args.Length = 0 then
        printfn "É necessário infomar o(s) arquivo(s) .pdb a ser(em) indexado(s)"
        1
    else
        for filePattern in args do
            let mat = regPattern.Match(filePattern)

            for pdbFile in Directory.EnumerateFiles((if mat.Groups.[1].Length = 0 then "." else mat.Groups.[1].Value), mat.Groups.[2].Value) do
                printfn "Indexando arquivo %s..." pdbFile
                if writeSrcsrv pdbFile then
                    printfn "Arquivo %s indexado" pdbFile
                else
                    printfn "Arquivo %s não foi indexado" pdbFile
        0

#if INTERACTIVE
main (Seq.skip 1 (fsi.CommandLineArgs) |> Seq.toArray)
#endif
