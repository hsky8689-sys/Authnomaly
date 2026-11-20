namespace Authnomaly.Tests;

//test module also benefits of .env variables
using System.Runtime.CompilerServices;

internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Load() => DotNetEnv.Env.NoClobber().TraversePath().Load();
}