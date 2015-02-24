# GitIndexer

## Objetives

This package aims to provide a easy way to have .pdb files with source indexed
with a Git repository. Once installed to a project, it should index .pdb files
on every build.

## Requirements

* Windows _(won't work with mono)_
* Visual Studio 2010 or higher
* PowerShell

## Configuring Visual Studio

For Visual Studio to able to use indexed symbol files, you must do the folowing
configuration:

* Tools menu
* Options
* Debuggin
* Check "Enable source server support" option


## Limitations

This is a beta release, with the following limitations:

* origin remote must be a GitHub repository (github.com or on-premise
  installation), using HTTP protocol.
* This GitHub repoitory must be public

Git protocol (and command line) tools does not provide a way for downloading a
single file. GitHub, in the other hand, provides it. This package will download
source using a URL like this:

    https://github.com/ecsousa/GitIndexer/raw/2fef0639a179357db1ea86f731293c930b45f086/GitIndexerTasks/GitIndex.fs

The extract command GitIndexer is using inside .pdb files, will expand to
somethig like this:

    C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -Command "& { (new-object System.Net.WebClient).DownloadFile('https://github.com/ecsousa/GitIndexer/raw/2fef0639a179357db1ea86f731293c930b45f086/GitIndexerTasks/GitIndex.fs', $args[0]) } "C:\Users\user\AppData\Local\Temp\SymbolCache\_HTTPS___GITHUB_COM_ECSOUSA_GITINDEXER\GitIndexerTasks\GitIndex.fs\2fef0639a179357db1ea86f731293c930b45f086\GitIndex.fs" "

As it does not uses git command line tool, it won't be able to get credentials
from git, and hence will only work with public repositories.

Suggestions on how to work arround these issues are welcome!

## Building

To build this package, you should go to `Package` directory, and execute the
following command (with Visual Studio environment variables loaded):

    msbuild /p:Version=<packageVersion>
