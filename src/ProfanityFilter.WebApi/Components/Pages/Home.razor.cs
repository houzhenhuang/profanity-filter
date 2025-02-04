﻿namespace ProfanityFilter.WebApi.Components.Pages;

public sealed partial class Home : IAsyncDisposable
{
    private readonly SystemTimer _debounceTimer = new()
    {
        Interval = 500,
        AutoReset = false
    };

    private readonly IObservable<ProfanityFilterRequest>? _liveRequests;
    private readonly ReplacementStrategy[] _strategies = Enum.GetValues<ReplacementStrategy>();
    private readonly CancellationTokenSource _cts = new();

    private HubConnection? _hub;
    private ReplacementStrategy _selectedStrategy;
    private string? _text = "Content to filter...";

    [Inject]
    public required ILogger<Home> Logger { get; set; }

    [Inject]
    public required NavigationManager Nav { get; set; }

    public Home()
    {
        _liveRequests = Observable.FromEventPattern<ElapsedEventHandler, ElapsedEventArgs>(
                handler => _debounceTimer.Elapsed += handler,
                handler => _debounceTimer.Elapsed -= handler
            )
            .Select(args => new ProfanityFilterRequest(
                _text,
                _selectedStrategy,
                0
            ));
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender is false)
        {
            return;
        }

        if (_hub is null)
        {
            var uri = new UriBuilder(Nav.BaseUri)
            {
                Path = "/profanity/hub"
            };

            _hub = new HubConnectionBuilder()
                .AddMessagePackProtocol()
                .WithUrl(uri.Uri)
                .WithAutomaticReconnect()
                .WithStatefulReconnect()
                .Build();

            _hub.Closed += OnHubConnectionClosedAsync;
            _hub.Reconnected += OnHubConnectionReconnectedAsync;
            _hub.Reconnecting += OnHubConnectionReconnectingAsync;

            await _hub.StartAsync();

            Logger.LogInformation("""
                Hub connection started.
                """);

            _ = StartLiveUpdateStreamAsync();
        }
    }

    private Task OnHubConnectionClosedAsync(Exception? exception)
    {
        Logger.LogInformation("""
            Hub connection closed: {Error}
            """,
            exception);

        return Task.CompletedTask;
    }

    private Task OnHubConnectionReconnectingAsync(Exception? exception)
    {
        Logger.LogWarning("""
            Hub connection reconnecting: {Error}
            """,
            exception);

        return Task.CompletedTask;
    }

    private Task OnHubConnectionReconnectedAsync(string? arg)
    {
        Logger.LogInformation("""
            Hub connection reconnected: {arg}
            """,
            arg);

        return Task.CompletedTask;
    }

    [MemberNotNullWhen(false, nameof(_hub))]
    private bool IsHubNotConnected()
    {
        if (_hub is null or not { State: HubConnectionState.Connected })
        {
            Logger.LogWarning("""
                Not connected to the profanity filter hub.
                """);

            return true;
        }

        return false;
    }

    private async Task StartLiveUpdateStreamAsync()
    {
        if (IsHubNotConnected() || _liveRequests is null)
        {
            return;
        }

        while (true)
        {
            try
            {
                // Producer requests...
                var liveRequests = _liveRequests.ToAsyncEnumerable();

                // Consumer responses...
                await foreach (var response in _hub.StreamAsync<ProfanityFilterResponse>(
                    methodName: "live", arg1: liveRequests, cancellationToken: _cts.Token))
                {
                    if (response is null)
                    {
                        break;
                    }

                    if (response is { FilteredText.Length: > 0 })
                    {
                        _text = response.FilteredText;
                    }

                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (Exception ex) when (Debugger.IsAttached)
            {
                _ = ex;

                Debugger.Break();

                throw;
            }
        }
    }

    private void OnTextChanged(ChangeEventArgs args)
    {
        _text = args?.Value?.ToString();

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public async ValueTask DisposeAsync()
    {
        _debounceTimer.Stop();
        _debounceTimer.Dispose();

        await _cts.CancelAsync();
        _cts.Dispose();

        if (_hub is not null)
        {
            _hub.Closed -= OnHubConnectionClosedAsync;
            _hub.Reconnected -= OnHubConnectionReconnectedAsync;
            _hub.Reconnecting -= OnHubConnectionReconnectingAsync;

            await _hub.StopAsync();
            await _hub.DisposeAsync();
        }
    }
}
