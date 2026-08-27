using UpxSharp.Wpf.ViewModels;
using Xunit;

namespace UpxSharp.Wpf.Tests;

public sealed class ProgressViewModelTests
{
    [Fact]
    public async Task ProgressUpdate_AnimatesLargeJumpToCompletion()
    {
        var viewModel = new ProgressViewModel();

        viewModel.UpdateCurrentFile("sample.exe", 84);
        await Task.Delay(320);

        viewModel.UpdateCurrentFile("sample.exe", 100);
        await Task.Delay(80);

        Assert.InRange(viewModel.CurrentFileProgressPercent, 84, 100);

        await Task.Delay(260);

        Assert.Equal(100, viewModel.CurrentFileProgressPercent);
    }

    [Fact]
    public async Task Clear_ResetsProgressWithoutWaitingForAnimation()
    {
        var viewModel = new ProgressViewModel();

        viewModel.UpdateOverallProgress(100);
        await Task.Delay(80);

        viewModel.Clear();

        Assert.Equal(0, viewModel.OverallProgressPercent);
        Assert.Equal(0, viewModel.CurrentFileProgressPercent);
    }
}
