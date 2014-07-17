using Git.Credential.WinStore;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace GitDownloader
{
    static class Extensions
    {
        public static string SHA1(this string inputString)
        {
            using(var sha1 = new System.Security.Cryptography.SHA1Managed())
            {
                var buffer = System.Text.Encoding.UTF8.GetBytes(inputString);
                var hash = sha1.ComputeHash(buffer);

                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Trace.Listeners.Add(new ConsoleTraceListener(useErrorStream : true));

            if(args.Length != 2)
            {
                Console.Error.WriteLine("GitDownloader requires 2 parameters:");
                Console.Error.WriteLine(" 1. Repository URI");
                Console.Error.WriteLine(" 2. File ID");
                Console.Error.WriteLine();
                Console.Error.WriteLine("File will be written to stdout");

                Environment.ExitCode = 1;
                return;
            }

            var repositoryUri = args[0];
            var fileID = args[1];

            var repositoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitDownloader", repositoryUri.SHA1());


            if(!Repository.IsValid(repositoryPath))
            {
                Console.Error.WriteLine("Could not file repository on '{0}'. Cloning it.", repositoryPath);
                try
                {
                    try
                    {
                        Repository.Clone(repositoryUri, repositoryPath, new CloneOptions { Checkout = false });
                    }
                    catch(Exception ex)
                    {
                        if(ex.Message == "Request failed with status code: 401")
                        {
                            Repository.Clone(repositoryUri, repositoryPath, new CloneOptions { Checkout = false, Credentials = RetrieveCredentials(repositoryUri) });
                        }
                        else throw;
                    }
                }
                catch(Exception ex)
                {
                    Console.Error.WriteLine("Error coling repository: {0}", ex.Message);
                    Environment.ExitCode = 1;
                    return;
                }
            }

            using(var repository = new Repository(repositoryPath))
            {
                var blob = repository.Lookup<Blob>(fileID);

                if(blob == null)
                {
                    Console.Error.WriteLine("Object {0} not found on local repository. Fetching origin remote for updates.", fileID);
                    try
                    {
                        repository.Fetch("origin");
                    }
                    catch(Exception ex)
                    {
                        if(ex.Message == "Request failed with status code: 401")
                        {
                            repository.Fetch("origin", new FetchOptions { Credentials = RetrieveCredentials(repositoryUri) });
                        }
                        else throw;
                    }
                    blob = repository.Lookup<Blob>(fileID);
                }

                if(blob == null)
                {
                    Console.Error.WriteLine("Object {0} not found on local repository after fetch. Exiting.", fileID);
                    Environment.ExitCode = 1;
                    return;
                }


                using(var content = blob.GetContentStream(new FilteringOptions(Path.Combine(repositoryPath, "output.txt"))))
                using(var output = Console.OpenStandardOutput())
                    content.CopyTo(output);
            }

        }

        private static Credentials RetrieveCredentials(string repositoryUri)
        {
            var completeUri = new Uri(repositoryUri);

            var info = Store.GetCommand(new Uri(completeUri, "/")).ToDictionary(
                t => t.Item1, t => t.Item2);

            return new UsernamePasswordCredentials
            {
                Username = info["username"],
                Password = info["password"]
            };

        }
    }
}
