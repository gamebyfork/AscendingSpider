using System;
using System.Collections.Generic;

namespace AscendingSpider.Domain;

public class Pile
{
    private readonly List<Card> _cards = new();
    private int _faceDownCount;

    public IReadOnlyList<Card> Cards => _cards;
    public int FaceDownCount => _faceDownCount;
    public int Count => _cards.Count;
    public bool IsEmpty => _cards.Count == 0;
    public Card TopCard => _cards[^1];

    public void AddFaceDown(Card card)
    {
        _cards.Add(card);
        _faceDownCount++;
    }

    public void AddFaceUp(Card card) => _cards.Add(card);

    public Card RemoveTop()
    {
        if (IsEmpty) throw new InvalidOperationException("Pile is empty");
        var card = _cards[^1];
        _cards.RemoveAt(_cards.Count - 1);
        AutoFlip();
        return card;
    }

    public Card RemoveAt(int index)
    {
        if (index < 0 || index >= _cards.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var card = _cards[index];
        _cards.RemoveAt(index);
        AutoFlip();
        return card;
    }

    private void AutoFlip()
    {
        if (_faceDownCount > 0 && _faceDownCount == _cards.Count)
            _faceDownCount--;
    }

    public Pile Clone()
    {
        var copy = new Pile { _faceDownCount = _faceDownCount };
        copy._cards.AddRange(_cards);
        return copy;
    }
}