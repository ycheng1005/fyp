using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// ============================================================================================
// Class: Player
// ============================================================================================

public class Player
{
    private const int STEP = 1;
    private const int FINISH = 5;
    public string Id { get; set; }
    public string Icon { get; set; }
    public string Name { get; set; }
    public List<int> BingoCard { get; set; }
    public int Count { get; set; } = 0;
    public bool IsWin => Count >= FINISH;
    public Player(string id, string icon, string name) => (Id, Icon, Name, BingoCard) = (id, icon, name, new List<int>());
    public void Run() => Count += STEP;
}

// ============================================================================================
// Class: Game
// ============================================================================================

public class Game
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public Player? PlayerA { get; set; } = null;
    public Player? PlayerB { get; set; } = null;
    public bool IsWaiting { get; set; } = false;
    public bool IsWin { get; set; } = false;
    public bool IsEmpty => PlayerA == null && PlayerB == null;
    public bool IsFull => PlayerA != null && PlayerB != null;
    private Dictionary<string, List<int>> playerBingoCards = new Dictionary<string, List<int>>();
    public string CurrentTurn { get; set; } = "A"; // Default to player A's turn



    private List<int> GenerateRandomBingoCard()
    {
        List<int> numbers = new List<int>();
        Random rand = new Random(Guid.NewGuid().GetHashCode()); // Initialize with a seed

        // Generate 25 unique random numbers between 1 and 25
        while (numbers.Count < 25)
        {
            int num = rand.Next(1, 26); // Adjust upper bound to include 25
            if (!numbers.Contains(num))
            {
                numbers.Add(num);
            }
        }

        return numbers;
    }

    public string? AddPlayer(Player player)
    {
        if (PlayerA == null)
        {
            PlayerA = player;
            PlayerA.BingoCard = GenerateRandomBingoCard();
            playerBingoCards[player.Id] = PlayerA.BingoCard;
            IsWaiting = true;
            return "A";
        }
        else if (PlayerB == null)
        {
            PlayerB = player;
            PlayerB.BingoCard = GenerateRandomBingoCard();
            playerBingoCards[player.Id] = PlayerB.BingoCard;
            IsWaiting = false;

            return "B";
        }
        return null;
    }


    public List<int>? GetPlayerBingoCard(string playerId)
    {
        if (playerBingoCards.ContainsKey(playerId))
        {
            return playerBingoCards[playerId];
        }
        return null;
    }

    public Dictionary<string, HashSet<int>> ClickedNumbers { get; } = new Dictionary<string, HashSet<int>>()
    {
        { "A", new HashSet<int>() },
        { "B", new HashSet<int>() }
    };

}



// ============================================================================================
// Class: GameHub 🐱🐶
// ============================================================================================

public class GameHub : Hub
{
    // ----------------------------------------------------------------------------------------
    // General
    // ----------------------------------------------------------------------------------------

    private static List<Game> games = new() { };

    // ----------------------------------------------------------------------------------------
    // Functions
    // ----------------------------------------------------------------------------------------

    public string Create()
    {
        var game = new Game();
        games.Add(game);
        return game.Id;
    }

    public async Task Run(string letter, int clickedNumber)
    {
        string gameId = Context.GetHttpContext()!.Request.Query["gameId"].ToString();
        var game = games.Find(g => g.Id == gameId);
        if (game == null)
        {
            await Clients.Caller.SendAsync("Reject");
            return;
        }

        if (game.CurrentTurn != letter)
        {
            // It's not this player's turn, reject the action
            await Clients.Caller.SendAsync("Reject");
            return;
        }

        var player = letter == "A" ? game.PlayerA : game.PlayerB;
        if (player == null) return;

        player.Run();

        // Check if both players have marked a number
        if (game.PlayerA != null && game.PlayerB != null)
        {
            // Both players have marked a number, check for winning lines
            await CheckLineWin("A", game.PlayerA.BingoCard);
            await CheckLineWin("B", game.PlayerB.BingoCard);
        }

    }

    public async Task RequestBingoCard()
    {
        string gameId = Context.GetHttpContext()!.Request.Query["gameId"].ToString();
        string playerId = Context.ConnectionId;

        var game = games.Find(g => g.Id == gameId);
        if (game != null)
        {
            var bingoCard = game.GetPlayerBingoCard(playerId);
            if (bingoCard != null)
            {
                await Clients.Caller.SendAsync("ReceiveBingoCard", bingoCard);
            }
        }
    }

    public async Task SendBingoCard(int[] bingoCard)
    {
        await Clients.All.SendAsync("ReceiveBingoCard", bingoCard);
    }

    public async Task MarkNumber(string player, int number)
    {
        string gameId = Context.GetHttpContext()!.Request.Query["gameId"].ToString();
        var game = games.Find(g => g.Id == gameId);
        if (game == null)
        {
            return;
        }

        if (game.CurrentTurn != player)
        {
            // It's not this player's turn, reject the action
            return;
        }

        var currentPlayer = player == "A" ? game.PlayerA : game.PlayerB;
        if (currentPlayer == null) return;

        // Mark the clicked number on the player's bingo card
        int index = currentPlayer.BingoCard.IndexOf(number);
        if (index != -1)
        {
            currentPlayer.BingoCard[index] = 0; // Mark the number as clicked (replace with 0)
            await Clients.Group(gameId).SendAsync("NumberMarked", player, number);

            // Add the clicked number to the set of clicked numbers for the current player
            game.ClickedNumbers[player].Add(number);

            // Check if the player has won after marking the number
            await CheckLineWin(player, currentPlayer.BingoCard);

            // Switch turn to the other player
            game.CurrentTurn = player == "A" ? "B" : "A";
        }
    }

    public async Task CheckLineWin(string player, List<int> bingoCard)
    {
        var game = games.Find(g => g.Id == Context.GetHttpContext().Request.Query["gameId"].ToString());
        if (game == null)
        {
            await Clients.Caller.SendAsync("Reject");
            return;
        }

        var currentPlayer = player == "A" ? game.PlayerA : game.PlayerB;
        if (currentPlayer == null) return;

        // Check for winning line
        if (CheckForWinningLine(currentPlayer.BingoCard))
        {
            await Clients.Group(game.Id).SendAsync("LineWin", player, bingoCard);
        }
    }


    private bool CheckForWinningLine(List<int> bingoCard)
    {
        // Define winning patterns
        int[][] winningPatterns = new int[][] {
        // Horizontal lines
        new int[] { 0, 1, 2, 3, 4 }, // Top row
        new int[] { 5, 6, 7, 8, 9 }, // Second row
        new int[] { 10, 11, 12, 13, 14 }, // Third row
        new int[] { 15, 16, 17, 18, 19 }, // Fourth row
        new int[] { 20, 21, 22, 23, 24 }, // Bottom row
        // Vertical lines
        new int[] { 0, 5, 10, 15, 20 }, // Leftmost column
        new int[] { 1, 6, 11, 16, 21 }, // Second column
        new int[] { 2, 7, 12, 17, 22 }, // Third column
        new int[] { 3, 8, 13, 18, 23 }, // Fourth column
        new int[] { 4, 9, 14, 19, 24 }, // Rightmost column
        // Diagonal lines
        new int[] { 0, 6, 12, 18, 24 }, // Main diagonal
        new int[] { 4, 8, 12, 16, 20 } // Secondary diagonal
    };

        foreach (var pattern in winningPatterns)
        {
            bool hasWinningLine = true;
            foreach (var number in pattern)
            {
                if (!bingoCard.Contains(number) || bingoCard.IndexOf(number) != -1)
                {
                    hasWinningLine = false;
                    break;
                }
            }
            if (hasWinningLine)
            {
                return true; // Winning line found
            }
        }

        return false; // No winning line found
    }



    //=========================Chat function===============================

    private static int count = 0;
    private const int MaxMessageHistoryCount = 10;
    private const int MessageCooldownSeconds = 3;
    private static HashSet<string> rudeWords = new HashSet<string>();
    private static List<(string, string, string)> messageHistory = new List<(string, string, string)>();
    private Dictionary<string, DateTime> userCooldowns = new Dictionary<string, DateTime>();

    static GameHub()
    {
        LoadRudeWordsFromFile("wwwroot/rude_words.txt");
    }

    private static void LoadRudeWordsFromFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            string[] words = File.ReadAllLines(filePath);
            rudeWords = new HashSet<string>(words.Select(w => w.ToLower()));
        }
    }

    private static bool ContainsRudeWords(string message)
    {
        string[] words = message.ToLower().Split(' ');
        foreach (string word in words)
        {
            if (rudeWords.Contains(word))
            {
                return true;
            }
        }
        return false;
    }


    public async Task SendText(string name, string message)
    {
        if (ContainsRudeWords(message))
        {
            // Handle rude words (e.g., censor or reject the message)
            await Clients.Caller.SendAsync("ReceiveText", "System", "Rude words message cannot be sent.");
        }
        else
        {
            // Check if the user is in cooldown
            if (userCooldowns.ContainsKey(name))
            {
                var lastMessageTime = userCooldowns[name];
                if ((DateTime.UtcNow - lastMessageTime).TotalSeconds < MessageCooldownSeconds)
                {
                    // User is in cooldown, prevent message from being sent
                    await Clients.Caller.SendAsync("ReceiveText", "System", "You are sending messages too quickly. Please wait.");
                    return;
                }
            }

            // Add the message to the message history
            AddMessageToHistory(name, message, "me");

            await Clients.Caller.SendAsync("ReceiveText", name, message, "caller");
            await Clients.Others.SendAsync("ReceiveText", name, message, "others");

            // Update user cooldown timestamp
            userCooldowns[name] = DateTime.UtcNow;
        }
    }

    private void AddMessageToHistory(string name, string message, string who)
    {

        // Check if the user is in cooldown
        if (userCooldowns.ContainsKey(name))
        {
            var lastMessageTime = userCooldowns[name];
            if ((DateTime.UtcNow - lastMessageTime).TotalSeconds < MessageCooldownSeconds)
            {
                // User is in cooldown, prevent message from being added to history
                return;
            }
        }

        // Add the message to the history and trim it if it exceeds the maximum count
        messageHistory.Add((name, message, who));
        while (messageHistory.Count > MaxMessageHistoryCount)
        {
            messageHistory.RemoveAt(0);
        }

        // Update user cooldown timestamp
        userCooldowns[name] = DateTime.UtcNow;
    }

    public async Task SendImage(string name, string url)
    {
        await Clients.Caller.SendAsync("ReceiveImage", name, url, "caller");
        await Clients.Others.SendAsync("ReceiveImage", name, url, "others");
    }

    public async Task SendYouTube(string name, string id)
    {
        await Clients.Caller.SendAsync("ReceiveYouTube", name, id, "caller");
        await Clients.Others.SendAsync("ReceiveYouTube", name, id, "others");
    }

    public async Task SendCapturedScreen(string base64Data)
    {
        // Process the captured screen data (e.g., store in a database, broadcast to other clients)
        await Clients.All.SendAsync("ReceiveCapturedScreen", base64Data);

        // Example: Save captured screen image to a file
        byte[] imageData = Convert.FromBase64String(base64Data.Split(',')[1]);
        string fileName = "captured_screen_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".png";
        string filePath = Path.Combine("captured_screens", fileName);
        File.WriteAllBytes(filePath, imageData);
    }

    public async Task SendDocument(string sender, string documentName, byte[] documentData)
    {
        // Process the received document data (e.g., store in a database, broadcast to other clients)
        await Clients.All.SendAsync("ReceiveDocument", sender, documentName, documentData);
    }

    // ----------------------------------------------------------------------------------------
    // Connected
    // ----------------------------------------------------------------------------------------

    public override async Task OnConnectedAsync()
    {
        string page = Context.GetHttpContext()!.Request.Query["page"].ToString();

        switch (page)
        {
            case "list": await ListConnected(); break;
            case "game": await GameConnected(); break;
        }
        count++;
        string name = Context.GetHttpContext()!.Request.Query["name"].ToString();
        await Clients.All.SendAsync("UpdateStatus", count, $"<b>{name}</b> joined");

        // Send message history to the new client upon connection
        foreach (var (messageName, message, who) in messageHistory)
        {
            await Clients.Client(Context.ConnectionId).SendAsync("ReceiveText", messageName, message, who);
        }
        await base.OnConnectedAsync();
    }

    private async Task ListConnected()
    {
        await Clients.Caller.SendAsync("UpdateList", games.FindAll(g => g.IsWaiting));
    }

    private async Task GameConnected()
    {
        string id = Context.ConnectionId;
        string icon = Context.GetHttpContext()!.Request.Query["icon"].ToString();
        string name = Context.GetHttpContext()!.Request.Query["name"].ToString();
        string gameId = Context.GetHttpContext()!.Request.Query["gameId"].ToString();

        var game = games.Find(g => g.Id == gameId);
        if (game == null || game.IsFull)
        {
            await Clients.Caller.SendAsync("Reject");
            return;
        }

        var player = new Player(id, icon, name);
        var letter = game.AddPlayer(player);

        await Groups.AddToGroupAsync(id, gameId);
        await Clients.Caller.SendAsync("Ready", letter, game); // Don't send bingo card here
        await Clients.All.SendAsync("UpdateList", games.FindAll(g => g.IsWaiting));

        if (game.IsFull)
        {
            // Send start signal or any other necessary operations
            await Clients.Group(gameId).SendAsync("Start");
        }
    }

    // ----------------------------------------------------------------------------------------
    // Disconnected
    // ----------------------------------------------------------------------------------------

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string page = Context.GetHttpContext()!.Request.Query["page"].ToString();

        switch (page)
        {
            case "list": await ListDisconnected(); break;
            case "game": await GameDisconnected(); break;
        }

        count--;
        string name = Context.GetHttpContext()!.Request.Query["name"].ToString();
        await Clients.All.SendAsync("UpdateStatus", count, $"<b>{name}</b> left");
        await base.OnDisconnectedAsync(exception);
    }

    private async Task ListDisconnected()
    {
        await Task.CompletedTask;
    }

    private async Task GameDisconnected()
    {
        string id = Context.ConnectionId;
        string gameId = Context.GetHttpContext()!.Request.Query["gameId"].ToString();

        var game = games.Find(g => g.Id == gameId);
        if (game == null)
        {
            return;
        }

        if (game.PlayerA?.Id == id)
        {
            game.PlayerA = null;
            await Clients.Group(gameId).SendAsync("Left", "A");
        }
        else if (game.PlayerB?.Id == id)
        {
            game.PlayerB = null;
            await Clients.Group(gameId).SendAsync("Left", "B");
        }

        if (game.IsEmpty)
        {
            games.Remove(game);
            messageHistory.Clear();
            await Clients.All.SendAsync("UpdateList", games.FindAll(g => g.IsWaiting));
        }
    }

    // End of GameHub -------------------------------------------------------------------------
}