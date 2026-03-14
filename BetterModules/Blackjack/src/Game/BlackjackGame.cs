using System;
using System.Collections.Generic;
using System.Threading;

namespace Blackjack.Game;

public record Card(string Rank, string Suit)
{
    public override string ToString() => $"{Rank}{Suit}";
}

public class BlackjackGameState
{
    public List<Card> Deck { get; } = new();
    public List<Card> PlayerHand { get; } = new();
    public List<Card> DealerHand { get; } = new();
    public int BetAmount { get; set; }
    public int SecondsRemaining { get; set; }
    public CancellationTokenSource? Timer { get; set; }
}

public static class BlackjackDeck
{
    public static readonly string[] Suits = ["♣", "♦", "♥", "♠"];
    public static readonly string[] Ranks = ["A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K"];
    public const string CardBackUrl = "https://raw.githubusercontent.com/vulikit/varkit-resources/refs/heads/main/bj/53.jpg";

    public static List<Card> Create()
    {
        var deck = new List<Card>(52);
        foreach (var suit in Suits)
            foreach (var rank in Ranks)
                deck.Add(new Card(rank, suit));
        Shuffle(deck);
        return deck;
    }

    private static void Shuffle(List<Card> deck)
    {
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }

    public static Card Draw(BlackjackGameState game)
    {
        if (game.Deck.Count == 0)
            game.Deck.AddRange(Create());
        var card = game.Deck[0];
        game.Deck.RemoveAt(0);
        return card;
    }

    public static int CalculateHand(List<Card> hand)
    {
        int total = 0, aces = 0;
        foreach (var card in hand)
        {
            if (card.Rank == "A") aces++;
            else if (card.Rank is "J" or "Q" or "K") total += 10;
            else total += int.Parse(card.Rank);
        }
        for (int i = 0; i < aces; i++)
            total += (total + 11 <= 21) ? 11 : 1;
        return total;
    }

    public static int GetCardValue(Card card, List<Card> fullHand)
    {
        if (card.Rank is "J" or "Q" or "K") return 10;
        if (card.Rank != "A") return int.Parse(card.Rank);

        int otherTotal = 0, otherAces = 0;
        foreach (var c in fullHand)
        {
            if (c == card) continue;
            if (c.Rank == "A") otherAces++;
            else if (c.Rank is "J" or "Q" or "K") otherTotal += 10;
            else otherTotal += int.Parse(c.Rank);
        }
        for (int i = 0; i < otherAces; i++)
            otherTotal += (otherTotal + 11 <= 21) ? 11 : 1;

        return (otherTotal + 11 <= 21) ? 11 : 1;
    }

    public static string GetCardImageUrl(Card card)
    {
        int suitIndex = Array.IndexOf(Suits, card.Suit);
        int rankIndex = Array.IndexOf(Ranks, card.Rank);
        int cardNumber = suitIndex * 13 + rankIndex + 1;
        return $"https://raw.githubusercontent.com/btnrv/BetterStore/refs/heads/main/BetterModules/Blackjack/bj/{cardNumber}.jpg";
    }
}
