using Avalonia.Controls;
using Avalonia.Interactivity;
using ShadUI;
using Xunit;

namespace ShadUI.Tests.Controls;

file sealed class TestWindow : Window
{
	public void RaiseLoaded() => OnLoaded(new RoutedEventArgs(Control.LoadedEvent));
	public void RaiseUnloaded() => OnUnloaded(new RoutedEventArgs(Control.UnloadedEvent));
}

public class DialogHostDecouplingTests
{
	[Fact]
	public void Window_HasOpenDialog_Reflects_Child_DialogHost_State()
	{
		AvaloniaTestFixture.RunOnUIThread(() =>
		{
			var host = new DialogHost();
			var window = new TestWindow();
			window.Hosts.Add(host);

			window.RaiseLoaded();

			Assert.False(window.HasOpenDialog);

			host.HasOpenDialog = true;
			Assert.True(window.HasOpenDialog);

			host.HasOpenDialog = false;
			Assert.False(window.HasOpenDialog);

			window.RaiseUnloaded();
		});
	}
}
