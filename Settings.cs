using ExileCore2.Shared.Attributes;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;

namespace RitualHelper;

public class Settings : ISettings
{
    public ToggleNode Enable { get; set; } = new(true);
    
    [Menu("Action Delay (ms)", "Delay between actions to simulate human behavior")]
    public RangeNode<int> ActionDelay { get; set; } = new(75, 50, 1000);

    [Menu("Random Delay (ms)", "Random delay added to action delay (0-100ms)")]
    public RangeNode<int> RandomDelay { get; set; } = new(25, 0, 100);

    [Menu("Cancel With Right Mouse Button", "Cancel operation on manual right-click")]
    public ToggleNode CancelWithRightClick { get; set; } = new(true);

    [Menu("Defer new items", "Defer items that are not already deferred")]
    public ToggleNode DeferNewItems { get; set; } = new(true);

    [Menu("Defer existing items", "Defer items that are already deferred")]
    public ToggleNode DeferExistingItems { get; set; } = new(true);

    [Menu("New Items Defer List", "Comma separated list to check against base item name")]
    public TextNode DeferNewItemsList { get; set; } = new("Perfect, Divine Orb, Exalted Orb, Chaos Orb, Omen of, 20, Petition Splinter");
}