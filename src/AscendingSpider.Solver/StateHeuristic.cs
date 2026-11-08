using AscendingSpider.Domain;

namespace AscendingSpider.Solver;

public static class StateHeuristic
{
    /// <summary>
    /// Чем меньше значение, тем лучше состояние для игрока.
    /// </summary>
    public static int Calculate(GameState state)
    {
        int score = 0;

        // 1. Собранные последовательности — это огромный плюс (уменьшаем вес)
        score -= state.CompletedSequences * 1000;

        for (int i = 0; i < 10; i++)
        {
            var pile = state.Tableau[i];
            
            // 2. Каждая закрытая карта — это штраф (мы не знаем, что там)
            score += pile.FaceDownCount * 10;

            // 3. Хаос в открытых картах. Если карты не образуют последовательность,
            // мы не можем их переносить друг на друга.
            if (pile.Count > 0)
            {
                var cards = pile.Cards;
                for (int j = pile.FaceDownCount; j < cards.Count - 1; j++)
                {
                    // Если карта j+1 не является продолжением карты j
                    if (!GameRules.CanPlaceOn(cards[j + 1], cards[j]) || cards[j].Suit != cards[j + 1].Suit)
                    {
                        score += 1; // Штраф за разрыв последовательности
                    }
                }
            }
        }

        // 4. Бонус за пустые колонки. Пустая колонка — это универсальное место для любой карты.
        int emptyColumns = 0;
        for (int i = 0; i < 10; i++)
            if (state.Tableau[i].IsEmpty) emptyColumns++;

        score -= emptyColumns * 15;

        return score;
    }
}