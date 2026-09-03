using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace GLoader
{
    internal static class GameBootstrap
    {
        public static Assembly Load(string targetPath)
        {
            Log.Info("Loading managed Terraria assembly into the 64-bit CoreCLR host.");
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(System.IO.Path.GetFullPath(targetPath));
        }

        public static int InvokeEntryPoint(Assembly gameAssembly, string[] gameArguments)
        {
            var entryPoint = gameAssembly.EntryPoint;
            if (entryPoint == null)
                throw new MissingMethodException("Terraria assembly has no managed entry point.");

            var parameters = entryPoint.GetParameters();
            object[] invokeArguments;

            if (parameters.Length == 0)
            {
                invokeArguments = null;
            }
            else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
            {
                invokeArguments = new object[] { gameArguments ?? Array.Empty<string>() };
            }
            else
            {
                throw new NotSupportedException(
                    "Unsupported Terraria entry point signature: " + entryPoint);
            }

            try
            {
                var result = entryPoint.Invoke(null, invokeArguments);

                if (result is Task<int> intTask)
                    return intTask.GetAwaiter().GetResult();

                if (result is Task task)
                {
                    task.GetAwaiter().GetResult();
                    return 0;
                }

                return result is int exitCode ? exitCode : 0;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
