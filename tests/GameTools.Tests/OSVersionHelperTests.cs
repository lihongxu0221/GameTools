using GameTools.Win32.Helpers;
using Xunit;

namespace GameTools.Tests;

public class OSVersionHelperTests
{
    [Fact]
    public void OSVersionHelper_ShouldReturnValidDescription()
    {
        string desc = OSVersionHelper.GetOsDescription();
        Assert.NotNull(desc);
        Assert.NotEmpty(desc);
    }

    [Fact]
    public void GetRecommendedPrintWindowFlags_ShouldReturnValidFlag()
    {
        uint normalFlag = OSVersionHelper.GetRecommendedPrintWindowFlags(clientAreaOnly: false);
        uint clientFlag = OSVersionHelper.GetRecommendedPrintWindowFlags(clientAreaOnly: true);

        Assert.Equal(0x00000001u, clientFlag);

        if (OSVersionHelper.IsWindows81OrGreater)
        {
            Assert.Equal(0x00000002u, normalFlag);
        }
        else
        {
            Assert.Equal(0x00000000u, normalFlag);
        }
    }
}
