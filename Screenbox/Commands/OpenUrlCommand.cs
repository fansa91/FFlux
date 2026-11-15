using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Screenbox.Core.Models;
using Screenbox.Dialogs;

namespace Screenbox.Commands;

/// <summary>
/// Opens the "open online link" dialog and forwards the result to a host view model.
/// </summary>
/// <remarks>
/// The dialog runs here rather than in a view model because it needs a <c>XamlRoot</c>; the
/// command is the seam that keeps <c>Screenbox.Core</c> free of UI types.
/// </remarks>
internal sealed partial class OpenUrlCommand : IRelayCommand
{
    /// <summary>
    /// Gets or sets the command that receives the URL together with its anti-hotlink fields.
    /// </summary>
    public IRelayCommand<OpenUrlRequest>? NextCommand { get; set; }

    private readonly AsyncRelayCommand _asyncCommand;

    public OpenUrlCommand()
    {
        _asyncCommand = new AsyncRelayCommand(OpenUrlAsync);
    }

    public bool CanExecute(object? parameter)
    {
        return _asyncCommand.CanExecute(parameter);
    }

    public void Execute(object? parameter)
    {
        _asyncCommand.Execute(parameter);
    }

    public event EventHandler? CanExecuteChanged;

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task OpenUrlAsync()
    {
        OpenUrlRequest? request = await OpenUrlDialog.GetRequestAsync();
        if (request is null) return;
        NextCommand?.Execute(request);
    }
}
