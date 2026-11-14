using System;
using System.Linq;
using AscendingSpider.Domain;
using Xunit;

namespace AscendingSpider.Tests;

/// <summary>
/// Regression tests for the current AscendingSpider rules:
/// one Spades suit and ascending Ace-to-King sequences.
/// </summary>
public sealed class SpiderClassicRulesTests
{
    [Fact]
    public void DealMatchesStandardSpiderOpeningLayout()
    {
        GameState state = DeckFactory.Deal(1, 5, new Random(12345));

        Assert.Equal(1, state.SuitCount);
        Assert.Equal(10, state.Tableau.Count);

        for (int column = 0; column < 4; column++)
            Assert.Equal(5, state.Tableau[column].FaceDownCount);

        for (int column = 4; column < 10; column++)
            Assert.Equal(4, state.Tableau[column].FaceDownCount);

        Assert.All(state.Tableau, pile => Assert.Equal(1, pile.Count - pile.FaceDownCount));
        Assert.Equal(54, state.Tableau.Sum(pile => pile.Count));
        Assert.Equal(44, state.Tableau.Sum(pile => pile.FaceDownCount));
        Assert.Equal(50, state.Stock.Count);
        Assert.Equal(104, state.Stock.Count + state.Tableau.Sum(pile => pile.Count));
        Assert.All(
            state.Tableau.SelectMany(pile => pile.Cards).Concat(state.Stock),
            card => Assert.Equal(Suit.Spades, card.Suit));
    }

    [Fact]
    public void CardsCanBePlacedInAscendingAceToKingOrder()
    {
        Assert.True(GameRules.CanPlaceOn(
            new Card(Suit.Spades, Rank.King),
            new Card(Suit.Spades, Rank.Queen)));

        Assert.False(GameRules.CanPlaceOn(
            new Card(Suit.Spades, Rank.Queen),
            new Card(Suit.Spades, Rank.King)));
    }

    [Fact]
    public void AscendingSameSuitRunCanBePickedUp()
    {
        var pile = new Pile();
        pile.AddFaceUp(new Card(Suit.Spades, Rank.Ace));
        pile.AddFaceUp(new Card(Suit.Spades, Rank.Two));
        pile.AddFaceUp(new Card(Suit.Spades, Rank.Three));

        Assert.True(GameRules.CanPickUp(pile, 0, SpiderRuleset.Classic));
        Assert.Equal(2, GameRules.GetSequenceEnd(pile, 0, SpiderRuleset.Classic));
    }

    [Fact]
    public void StockDealAddsOneFaceUpCardToEachColumn()
    {
        GameState state = DeckFactory.Deal(1, 5, new Random(54321));
        int[] oldCounts = state.Tableau.Select(pile => pile.Count).ToArray();

        Assert.True(GameEngine.TryDealFromStock(state));

        for (int column = 0; column < 10; column++)
            Assert.Equal(oldCounts[column] + 1, state.Tableau[column].Count);

        Assert.Equal(40, state.Stock.Count);
        Assert.Equal(1, state.DealsMade);
    }

    [Fact]
    public void CompleteSequenceMustBeAceThroughKing()
    {
        var ascending = new Pile();
        for (int rank = 1; rank <= 13; rank++)
            ascending.AddFaceUp(new Card(Suit.Spades, (Rank)rank));

        Assert.True(GameRules.HasCompleteSequence(ascending));

        var descending = new Pile();
        for (int rank = 13; rank >= 1; rank--)
            descending.AddFaceUp(new Card(Suit.Spades, (Rank)rank));

        Assert.False(GameRules.HasCompleteSequence(descending));
    }
}
