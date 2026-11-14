using TUnit.Core;

namespace Screenbox.Core.Tests.Helpers;

public static class TestInitializer
{
    [BeforeEvery(HookType.Test)]
    public static void InitializeTest()
    {
        // Core business logic must never touch a WinRT dispatcher activation
        // factory. Install the managed implementation before every test so
        // providers replaced by an individual test cannot leak into the next one.
        TestAppDispatcher.Install();
    }
}
