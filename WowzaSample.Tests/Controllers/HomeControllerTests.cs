using Microsoft.AspNetCore.Mvc;
using WowzaSample.Controllers;

using Xunit;

namespace WowzaSample.Tests.Controllers;

public class HomeControllerTests
{
    [Fact]
    public void Index_ReturnsViewResult()
    {
        var controller = new HomeController();

        var result = controller.Index();

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Null(viewResult.ViewName); // Default view name = action name
    }
}
