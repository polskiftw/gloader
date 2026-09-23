using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace GLoader
{
    internal static class GameBootstrap
    {
        public static Assembly Load(string targetPath)
        {
            Log.Info("Loading Terraria into the current Mono AppDomain.");
            return Assembly.LoadFrom(targetPath);
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
                throw new NotSupportedException("Unsupported Terraria entry point signature: " + entryPoint);
            }

            try
            {
                var result = entryPoint.Invoke(null, invokeArguments);

                var intTask = result as Task<int>;
                if (intTask != null)
                    return intTask.GetAwaiter().GetResult();

                var task = result as Task;
                if (task != null)
                {
                    task.GetAwaiter().GetResult();
                    return 0;
                }

                return result is int ? (int)result : 0;
            }
            catch (TargetInvocationException ex)
            {
                if (ex.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                }

                throw;
            }
        }
    }
}
