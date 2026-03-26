#nullable enable

using MovieApp.Core.Interfaces;
using MovieApp.Core.Models;
using MovieApp.UI.Commands;
using MovieApp.UI.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace MovieApp.UI.ViewModels;

/// <summary>
/// Drives the battle arena view.
/// </summary>
public class BattleViewModel : ViewModelBase
{
    private const int LoggedInUserId = 1;

    private readonly IBattleService _battleService;
    private readonly IPointService _pointService;
    private readonly GamificationRefreshService _refreshService;
    private Battle? _activeBattle;
    private string _statusMessage = "Loading battle arena...";
    private bool _isBetPanelVisible;
    private string _betAmount = string.Empty;
    private string _betValidationMessage = string.Empty;
    private BattleMovieChoice? _selectedMovieChoice;
    private int _currentPoints;
    private bool _isBetAmountValid;
    private bool _hasPlacedBet;

    /// <summary>
    /// Initializes a new instance of the <see cref="BattleViewModel"/> class.
    /// </summary>
    public BattleViewModel(IBattleService battleService, IPointService pointService, GamificationRefreshService refreshService)
    {
        _battleService = battleService;
        _pointService = pointService;
        _refreshService = refreshService;
        _refreshService.RefreshRequested += OnRefreshRequested;

        RefreshCommand = new AsyncRelayCommand(_ => LoadBattleAsync());
        ToggleBetPanelCommand = new RelayCommand(_ => ToggleBetPanel(), _ => HasActiveBattle && !HasPlacedBet);
        ConfirmBetCommand = new AsyncRelayCommand(_ => ConfirmBetAsync(), _ => HasActiveBattle && _isBetAmountValid && !HasPlacedBet);

        _ = LoadBattleAsync();
    }

    public Battle? ActiveBattle
    {
        get => _activeBattle;
        private set
        {
            if (SetProperty(ref _activeBattle, value))
            {
                OnPropertyChanged(nameof(HasActiveBattle));
                OnPropertyChanged(nameof(FirstMovie));
                OnPropertyChanged(nameof(SecondMovie));
                (ToggleBetPanelCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ConfirmBetCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasActiveBattle => ActiveBattle is not null;

    public bool HasPlacedBet
    {
        get => _hasPlacedBet;
        private set
        {
            if (SetProperty(ref _hasPlacedBet, value))
            {
                if (value)
                {
                    IsBetPanelVisible = false;
                    if (!string.IsNullOrWhiteSpace(BetAmount))
                    {
                        BetAmount = string.Empty;
                    }

                    if (!string.IsNullOrEmpty(BetValidationMessage))
                    {
                        BetValidationMessage = string.Empty;
                    }
                    _isBetAmountValid = false;
                }

                (ToggleBetPanelCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ConfirmBetCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public Movie? FirstMovie => ActiveBattle?.FirstMovie;

    public Movie? SecondMovie => ActiveBattle?.SecondMovie;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBetPanelVisible
    {
        get => _isBetPanelVisible;
        set => SetProperty(ref _isBetPanelVisible, value);
    }

    public string BetAmount
    {
        get => _betAmount;
        set
        {
            if (SetProperty(ref _betAmount, value))
            {
                UpdateBetAmountValidation(value);
            }
        }
    }

    public string BetValidationMessage
    {
        get => _betValidationMessage;
        private set => SetProperty(ref _betValidationMessage, value);
    }

    public BattleMovieChoice? SelectedMovieChoice
    {
        get => _selectedMovieChoice;
        set => SetProperty(ref _selectedMovieChoice, value);
    }

    public int CurrentPoints
    {
        get => _currentPoints;
        private set
        {
            if (SetProperty(ref _currentPoints, value))
            {
                UpdateBetAmountValidation(BetAmount);
            }
        }
    }

    public ObservableCollection<BattleMovieChoice> MovieChoices { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand ToggleBetPanelCommand { get; }

    public ICommand ConfirmBetCommand { get; }

    private async Task LoadBattleAsync()
    {
        try
        {
            ActiveBattle = await _battleService.GetActiveBattle();
            CurrentPoints = (await _pointService.GetUserStats(LoggedInUserId)).TotalPoints;

            MovieChoices.Clear();

            if (ActiveBattle is not null)
            {
                if (ActiveBattle.FirstMovie is not null)
                {
                    MovieChoices.Add(new BattleMovieChoice { MovieId = ActiveBattle.FirstMovie.MovieId, Title = ActiveBattle.FirstMovie.Title });
                }

                if (ActiveBattle.SecondMovie is not null)
                {
                    MovieChoices.Add(new BattleMovieChoice { MovieId = ActiveBattle.SecondMovie.MovieId, Title = ActiveBattle.SecondMovie.Title });
                }

                SelectedMovieChoice = MovieChoices.FirstOrDefault();

                var existingBet = await _battleService.GetBet(LoggedInUserId, ActiveBattle.BattleId);
                if (existingBet is not null)
                {
                    HasPlacedBet = true;
                    var betMovieTitle = MovieChoices.FirstOrDefault(choice => choice.MovieId == existingBet.MovieId)?.Title ?? "this movie";
                    StatusMessage = $"You already placed {existingBet.Amount} points on {betMovieTitle}.";
                }
                else
                {
                    HasPlacedBet = false;
                    StatusMessage = "An active battle is live this week.";
                }
            }
            else
            {
                IsBetPanelVisible = false;
                HasPlacedBet = false;
                StatusMessage = "No active battle this week.";
            }
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task ConfirmBetAsync()
    {
        if (ActiveBattle is null || SelectedMovieChoice is null || HasPlacedBet)
        {
            return;
        }

        if (!_isBetAmountValid || !int.TryParse(BetAmount, out var amount))
        {
            UpdateBetAmountValidation(BetAmount);
            return;
        }

        try
        {
            await _battleService.PlaceBet(LoggedInUserId, ActiveBattle.BattleId, SelectedMovieChoice.MovieId, amount);
            BetAmount = string.Empty;
            IsBetPanelVisible = false;
            HasPlacedBet = true;
            _refreshService.RequestRefresh();
            StatusMessage = "Bet placed successfully.";
            await LoadBattleAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async void OnRefreshRequested(object? sender, EventArgs e)
    {
        await LoadBattleAsync();
    }

    private void ToggleBetPanel()
    {
        if (!IsBetPanelVisible)
        {
            BetAmount = string.Empty;
            BetValidationMessage = string.Empty;
            _isBetAmountValid = false;
            (ConfirmBetCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        IsBetPanelVisible = !IsBetPanelVisible;
    }

    private void UpdateBetAmountValidation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            BetValidationMessage = string.Empty;
            _isBetAmountValid = false;
        }
        else if (!int.TryParse(value, out var parsedAmount))
        {
            BetValidationMessage = "Use digits only (no decimals).";
            _isBetAmountValid = false;
        }
        else if (parsedAmount <= 0)
        {
            BetValidationMessage = "Bet amount must be greater than zero.";
            _isBetAmountValid = false;
        }
        else if (parsedAmount > CurrentPoints)
        {
            BetValidationMessage = CurrentPoints > 0
                ? $"You only have {CurrentPoints} points available."
                : "You do not have any points to wager.";
            _isBetAmountValid = false;
        }
        else
        {
            BetValidationMessage = string.Empty;
            _isBetAmountValid = true;
        }

        (ConfirmBetCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }
}
