using CCStats.Desktop.ViewModels;

namespace CCStats.Tests;

public class AccountItemViewModelTests
{
    private static AccountItemViewModel CreateVm(string displayName = "My Account")
    {
        return new AccountItemViewModel { DisplayName = displayName };
    }

    // --- StartEditing ---

    [Fact]
    public void StartEditing_SetsIsEditingTrue()
    {
        var vm = CreateVm();

        vm.StartEditing();

        Assert.True(vm.IsEditing);
    }

    [Fact]
    public void StartEditing_CopiesDisplayNameToEditingName()
    {
        var vm = CreateVm("Alpha");

        vm.StartEditing();

        Assert.Equal("Alpha", vm.EditingName);
    }

    [Fact]
    public void StartEditing_SetsIsDirtyFalse()
    {
        var vm = CreateVm();

        vm.StartEditing();

        Assert.False(vm.IsDirty);
    }

    // --- CancelEditing ---

    [Fact]
    public void CancelEditing_SetsIsEditingFalse()
    {
        var vm = CreateVm();
        vm.StartEditing();

        vm.CancelEditing();

        Assert.False(vm.IsEditing);
    }

    [Fact]
    public void CancelEditing_SetsIsDirtyFalse()
    {
        var vm = CreateVm();
        vm.StartEditing();
        vm.EditingName = "Changed";

        vm.CancelEditing();

        Assert.False(vm.IsDirty);
    }

    // --- SaveName ---

    [Fact]
    public void SaveName_TrimsWhitespace_UpdatesDisplayName_ReturnsTrue()
    {
        var vm = CreateVm("Old");
        vm.StartEditing();
        vm.EditingName = "  New Name  ";

        var result = vm.SaveName();

        Assert.True(result);
        Assert.Equal("New Name", vm.DisplayName);
    }

    [Fact]
    public void SaveName_FiresNameChangedEvent()
    {
        var vm = CreateVm("Old");
        vm.StartEditing();
        vm.EditingName = "New Name";

        string? received = null;
        vm.NameChanged += (_, name) => received = name;

        vm.SaveName();

        Assert.Equal("New Name", received);
    }

    [Fact]
    public void SaveName_SetsIsEditingFalse()
    {
        var vm = CreateVm();
        vm.StartEditing();
        vm.EditingName = "Updated";

        vm.SaveName();

        Assert.False(vm.IsEditing);
    }

    [Fact]
    public void SaveName_SetsIsDirtyFalse()
    {
        var vm = CreateVm();
        vm.StartEditing();
        vm.EditingName = "Updated";

        vm.SaveName();

        Assert.False(vm.IsDirty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void SaveName_WithEmptyOrWhitespace_ReturnsFalse(string input)
    {
        var vm = CreateVm("Original");
        vm.StartEditing();
        vm.EditingName = input;

        var result = vm.SaveName();

        Assert.False(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SaveName_WithEmptyOrWhitespace_DoesNotFireNameChanged(string input)
    {
        var vm = CreateVm("Original");
        vm.StartEditing();
        vm.EditingName = input;

        bool fired = false;
        vm.NameChanged += (_, _) => fired = true;

        vm.SaveName();

        Assert.False(fired);
    }

    [Fact]
    public void SaveName_WithEmptyOrWhitespace_DoesNotChangeDisplayName()
    {
        var vm = CreateVm("Original");
        vm.StartEditing();
        vm.EditingName = "   ";

        vm.SaveName();

        Assert.Equal("Original", vm.DisplayName);
    }

    // --- EditingName / IsDirty ---

    [Fact]
    public void EditingName_WhenDiffersFromDisplayName_SetsIsDirtyTrue()
    {
        var vm = CreateVm("Hello");
        vm.StartEditing();

        vm.EditingName = "Different";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void EditingName_WhenMatchesDisplayName_IsDirtyStaysFalse()
    {
        var vm = CreateVm("Hello");
        vm.StartEditing();
        vm.EditingName = "Changed";

        vm.EditingName = "Hello";

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void EditingName_WhitespaceAroundMatchingName_SetsIsDirtyFalse()
    {
        // Trim logic: " Hello " trims to "Hello" which matches DisplayName
        var vm = CreateVm("Hello");
        vm.StartEditing();

        vm.EditingName = " Hello ";

        Assert.False(vm.IsDirty);
    }

    // --- NameChanged event ---

    [Fact]
    public void NameChanged_FiresWithCorrectTrimmedName()
    {
        var vm = CreateVm("Old");
        vm.StartEditing();
        vm.EditingName = "  Trimmed  ";

        string? eventArg = null;
        vm.NameChanged += (_, name) => eventArg = name;

        vm.SaveName();

        Assert.Equal("Trimmed", eventArg);
    }

    [Fact]
    public void NameChanged_SenderIsTheViewModel()
    {
        var vm = CreateVm("Old");
        vm.StartEditing();
        vm.EditingName = "New";

        object? sender = null;
        vm.NameChanged += (s, _) => sender = s;

        vm.SaveName();

        Assert.Same(vm, sender);
    }

    // --- Full flows ---

    [Fact]
    public void FullFlow_StartEditing_Type_SaveName_UpdatesDisplayName()
    {
        var vm = CreateVm("Original");

        vm.StartEditing();
        Assert.True(vm.IsEditing);
        Assert.Equal("Original", vm.EditingName);

        vm.EditingName = "Renamed";
        Assert.True(vm.IsDirty);

        var saved = vm.SaveName();

        Assert.True(saved);
        Assert.Equal("Renamed", vm.DisplayName);
        Assert.False(vm.IsEditing);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void FullFlow_StartEditing_Type_CancelEditing_DisplayNameUnchanged()
    {
        var vm = CreateVm("Original");

        vm.StartEditing();
        vm.EditingName = "Something Else";
        Assert.True(vm.IsDirty);

        vm.CancelEditing();

        Assert.Equal("Original", vm.DisplayName);
        Assert.False(vm.IsEditing);
        Assert.False(vm.IsDirty);
    }
}
