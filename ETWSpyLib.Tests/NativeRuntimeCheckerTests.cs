namespace ETWSpyLib.Tests;

public class NativeRuntimeCheckerTests
{
    [Fact]
    public void IsEtwRuntimeAvailable_DoesNotThrow()
    {
        // The probe must never propagate an exception - reporting the problem is its whole
        // purpose, so throwing would reintroduce the crash it exists to prevent.
        var exception = Record.Exception(() => NativeRuntimeChecker.IsEtwRuntimeAvailable());

        Assert.Null(exception);
    }

    [Fact]
    public void IsEtwRuntimeAvailable_ReturnsTrueOnBuildMachine()
    {
        // The test host already loads krabsetw for the other suites, so the native runtime
        // is by definition present here.
        Assert.True(NativeRuntimeChecker.IsEtwRuntimeAvailable());
    }

    [Fact]
    public void IsEtwRuntimeAvailable_IsStableAcrossCalls()
    {
        bool first = NativeRuntimeChecker.IsEtwRuntimeAvailable();
        bool second = NativeRuntimeChecker.IsEtwRuntimeAvailable();

        Assert.Equal(first, second);
    }

    [Fact]
    public void MissingRuntimeMessage_ContainsInstallCommand()
    {
        Assert.Contains(NativeRuntimeChecker.InstallCommand, NativeRuntimeChecker.MissingRuntimeMessage);
    }
}
