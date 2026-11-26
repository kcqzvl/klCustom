using System;
using System.IO;

namespace KlCustom.Installer
{
    internal static class RuntimeSetup
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length < 2)
                    throw new ArgumentException("Expected an operation and installation directory.");

                string directory = Path.GetFullPath(args[1]);
                switch (args[0])
                {
                    case "ensure":
                        RuntimePackage.Ensure(directory, args[2]);
                        break;
                    case "rollback":
                        RuntimePackage.Remove(directory, args[2]);
                        break;
                    case "commit":
                        RuntimePackage.Commit(directory, args[2]);
                        if (args.Length == 4 && args[3] != ".")
                        {
                            string previousDirectory = Path.GetFullPath(args[3]);
                            if (!string.Equals(directory.TrimEnd('\\'), previousDirectory.TrimEnd('\\'),
                                StringComparison.OrdinalIgnoreCase))
                                RuntimePackage.Remove(previousDirectory, null);
                        }
                        break;
                    case "remove":
                        RuntimePackage.Remove(directory, null);
                        break;
                    case "archive":
                        RuntimePackage.Provision(directory, null);
                        break;
                    default:
                        throw new ArgumentException("Unknown runtime setup operation.");
                }

                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }
    }
}
