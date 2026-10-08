using GameTools.Infrastructure.Input;
using Xunit;

namespace GameTools.Tests;

public class InputSimulatorTests
{
    [Fact]
    public void InputSimulator_SendNullOrEmptyText_ShouldNotThrow()
    {
        var simulator = new WindowsInputSimulator();
        simulator.SendText(string.Empty);
        simulator.PostTextToWindow(IntPtr.Zero, string.Empty);
    }
}
