using Mieruka.Core;
using Xunit;

namespace Mieruka.Tests;

public class CoreMarkerTests
{
    [Fact]
    public void Instance_Is_Not_Null()
    {
        var marker = new CoreMarker();
        Assert.NotNull(marker);
    }
}
