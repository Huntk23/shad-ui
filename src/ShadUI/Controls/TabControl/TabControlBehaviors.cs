using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

// ReSharper disable once CheckNamespace
namespace ShadUI;

/// <summary>
///     Attached behaviors for the themed <see cref="TabControl" />.
/// </summary>
public static class TabControlBehaviors
{
	/// <summary>
	///     When set, the TabControl theme tracks the selected item with a single sliding pill that
	///     stretches across both source and target during the transition before settling.
	/// </summary>
	public static readonly AttachedProperty<bool> UseSlidingPillProperty =
		AvaloniaProperty.RegisterAttached<TabControl, bool>(
			"UseSlidingPill", typeof(TabControlBehaviors));

	private static readonly Thickness PillInset = new(2, 0, 2, 4);
	private static readonly TimeSpan StretchHold = TimeSpan.FromMilliseconds(140);

	private static readonly ConditionalWeakTable<TabControl, PillState> States = new();

	static TabControlBehaviors()
	{
		UseSlidingPillProperty.Changed.AddClassHandler<TabControl>(OnUseSlidingPillChanged);
	}

	/// <summary>Reads the value of <see cref="UseSlidingPillProperty" /> on the given TabControl.</summary>
	public static bool GetUseSlidingPill(TabControl element) => element.GetValue(UseSlidingPillProperty);

	/// <summary>Sets the value of <see cref="UseSlidingPillProperty" /> on the given TabControl.</summary>
	public static void SetUseSlidingPill(TabControl element, bool value) =>
		element.SetValue(UseSlidingPillProperty, value);

	private static void OnUseSlidingPillChanged(TabControl tc, AvaloniaPropertyChangedEventArgs e)
	{
		if (e.NewValue is true)
			Attach(tc);
		else
			Detach(tc);
	}

	private static void Attach(TabControl tc)
	{
		if (States.TryGetValue(tc, out _)) return;
		States.Add(tc, new PillState());
		tc.TemplateApplied += OnTemplateApplied;
		tc.SelectionChanged += OnSelectionChanged;
		tc.LayoutUpdated += OnLayoutUpdated;
	}

	private static void Detach(TabControl tc)
	{
		tc.TemplateApplied -= OnTemplateApplied;
		tc.SelectionChanged -= OnSelectionChanged;
		tc.LayoutUpdated -= OnLayoutUpdated;
		States.Remove(tc);
	}

	private static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
	{
		if (sender is not TabControl tc || !States.TryGetValue(tc, out var state)) return;

		state.Pill = e.NameScope.Find<Border>("PART_SelectionPill");
		state.ItemsPresenter = e.NameScope.Find<ItemsPresenter>("PART_ItemsPresenter");
		if (state.Pill is null) return;

		state.SavedTransitions = state.Pill.Transitions;
		state.Pill.Transitions = null;
		state.HasInitialPosition = false;

		Dispatcher.UIThread.Post(() => UpdatePill(tc, animate: false), DispatcherPriority.Loaded);
	}

	private static void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (sender is TabControl tc) UpdatePill(tc, animate: true);
	}

	private static void OnLayoutUpdated(object? sender, EventArgs e)
	{
		if (sender is not TabControl tc || !States.TryGetValue(tc, out var state)) return;
		if (state.Pill is null || state.ItemsPresenter is null) return;

		var rect = MeasureSelected(tc, state);
		if (rect is null) return;
		if (rect.Value == state.LastTarget) return;

		UpdatePill(tc, animate: false);
	}

	private static void UpdatePill(TabControl tc, bool animate)
	{
		if (!States.TryGetValue(tc, out var state)) return;
		if (state.Pill is null || state.ItemsPresenter is null) return;

		var rect = MeasureSelected(tc, state);
		if (rect is null)
		{
			state.Pill.Opacity = 0;
			state.HasInitialPosition = false;
			return;
		}

		var target = rect.Value;

		if (!state.HasInitialPosition)
		{
			OpenPill(state, target);
		}
		else if (animate)
		{
			StretchAndSnap(state, target);
		}
		else
		{
			SetBounds(state.Pill, target.X, target.Y, target.Width, target.Height);
			state.Pill.Opacity = 1;
		}

		state.LastTarget = target;
		state.HasInitialPosition = true;
	}

	private static void OpenPill(PillState state, Rect target)
	{
		if (state.Pill is null) return;

		state.Pill.Transitions = null;
		var midX = target.X + target.Width * 0.5;
		SetBounds(state.Pill, midX, target.Y, 0, target.Height);
		state.Pill.Opacity = 0;

		var pill = state.Pill;
		var saved = state.SavedTransitions;

		Dispatcher.UIThread.Post(
			() =>
			{
				pill.Transitions = saved;
				Dispatcher.UIThread.Post(
					() =>
					{
						SetBounds(pill, target.X, target.Y, target.Width, target.Height);
						pill.Opacity = 1;
					},
					DispatcherPriority.Background);
			},
			DispatcherPriority.Background);
	}

	private static void StretchAndSnap(PillState state, Rect target)
	{
		if (state.Pill is null) return;

		var spanX = Math.Min(state.LastTarget.X, target.X);
		var spanRight = Math.Max(state.LastTarget.Right, target.Right);
		SetBounds(state.Pill, spanX, target.Y, spanRight - spanX, target.Height);
		state.Pill.Opacity = 1;

		var pill = state.Pill;
		DispatcherTimer.RunOnce(
			() => SetBounds(pill, target.X, target.Y, target.Width, target.Height),
			StretchHold);
	}

	private static Rect? MeasureSelected(TabControl tc, PillState state)
	{
		if (tc.SelectedIndex < 0 || state.ItemsPresenter is null) return null;
		var container = tc.ContainerFromIndex(tc.SelectedIndex);
		if (container is null || !container.IsVisible || container.Bounds.Width <= 0) return null;

		var topLeft = container.TranslatePoint(default, state.ItemsPresenter);
		if (topLeft is null) return null;

		var x = topLeft.Value.X + PillInset.Left;
		var y = topLeft.Value.Y + PillInset.Top;
		var w = Math.Max(0, container.Bounds.Width - PillInset.Left - PillInset.Right);
		var h = Math.Max(0, container.Bounds.Height - PillInset.Top - PillInset.Bottom);
		return new Rect(x, y, w, h);
	}

	private static void SetBounds(Border pill, double x, double y, double w, double h)
	{
		Canvas.SetLeft(pill, x);
		Canvas.SetTop(pill, y);
		pill.Width = w;
		pill.Height = h;
	}

	private sealed class PillState
	{
		public Border? Pill;
		public ItemsPresenter? ItemsPresenter;
		public Transitions? SavedTransitions;
		public Rect LastTarget;
		public bool HasInitialPosition;
	}
}
