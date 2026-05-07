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

	[Fact]
	public void Window_Tracks_DialogHosts_Added_After_Load()
	{
		AvaloniaTestFixture.RunOnUIThread(() =>
		{
			var window = new TestWindow();
			window.RaiseLoaded();

			Assert.False(window.HasOpenDialog);

			var host = new DialogHost();
			window.Hosts.Add(host);

			host.HasOpenDialog = true;
			Assert.True(window.HasOpenDialog);

			window.RaiseUnloaded();
		});
	}

	[Fact]
	public void Window_Untracks_DialogHosts_Removed_From_Hosts()
	{
		AvaloniaTestFixture.RunOnUIThread(() =>
		{
			var host = new DialogHost();
			var window = new TestWindow();
			window.Hosts.Add(host);
			window.RaiseLoaded();

			host.HasOpenDialog = true;
			Assert.True(window.HasOpenDialog);

			window.Hosts.Remove(host);
			// Aggregator should recompute and drop to false even though host stays open.
			Assert.False(window.HasOpenDialog);

			window.RaiseUnloaded();
		});
	}

	[Fact]
	public void DialogHost_Has_No_Owner_Property()
	{
		var hasOwner = typeof(DialogHost).GetProperty("Owner") is not null;
		Assert.False(hasOwner, "DialogHost.Owner must remain removed (decoupling guard).");
	}
}
