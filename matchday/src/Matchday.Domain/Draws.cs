namespace Matchday.Domain;

/// <summary>
/// K.-o.-Baum mit Freilosen. Übernommen aus dem alten Baum, ohne Setzliste:
/// die Reihenfolge der Teilnehmer ist bereits gemischt, und die Freilose
/// fallen an die ersten Positionen dieser Reihenfolge.
/// </summary>
public static class KnockoutDraw
{
    public static List<Match> Build(IReadOnlyList<Participant> order)
    {
        var bracketSize = NextPowerOfTwo(order.Count);
        var rounds = (int)Math.Log2(bracketSize);
        var matches = new List<Match>();

        // Erste Runde: Positionen in Setzreihenfolge besetzen, überzählige sind Freilose.
        var seedOrder = SeedOrder(bracketSize);
        var slots = new Side[bracketSize];

        for (var slot = 0; slot < bracketSize; slot++)
        {
            var number = seedOrder[slot];
            slots[slot] = number <= order.Count ? Side.Of(order[number - 1].Id) : Side.Bye;
        }

        var previousRound = new List<Match>();

        for (var position = 0; position < bracketSize / 2; position++)
        {
            previousRound.Add(new Match(
                Guid.NewGuid(), 1, position + 1, Label(1, rounds, position + 1, bracketSize / 2),
                slots[position * 2], slots[(position * 2) + 1]));
        }

        matches.AddRange(previousRound);

        for (var round = 2; round <= rounds; round++)
        {
            var count = previousRound.Count / 2;
            var thisRound = new List<Match>();

            for (var position = 0; position < count; position++)
            {
                thisRound.Add(new Match(
                    Guid.NewGuid(), round, position + 1, Label(round, rounds, position + 1, count),
                    Side.WinnerOf(previousRound[position * 2].Id),
                    Side.WinnerOf(previousRound[(position * 2) + 1].Id)));
            }

            matches.AddRange(thisRound);
            previousRound = thisRound;
        }

        return matches;
    }

    /// <summary>
    /// Setznummer je Bracket-Position, für acht Plätze <c>1, 8, 4, 5, 2, 7, 3, 6</c>.
    /// Rekursiv: das Bracket der Größe 2n entsteht aus dem der Größe n, indem
    /// jede Nummer s durch das Paar (s, 2n+1-s) ersetzt wird.
    /// </summary>
    internal static IReadOnlyList<int> SeedOrder(int bracketSize)
    {
        var order = new List<int> { 1 };

        while (order.Count < bracketSize)
        {
            var size = order.Count * 2;
            var next = new List<int>(size);

            foreach (var seed in order)
            {
                next.Add(seed);
                next.Add(size + 1 - seed);
            }

            order = next;
        }

        return order;
    }

    public static int NextPowerOfTwo(int count)
    {
        var size = 1;
        while (size < count)
        {
            size *= 2;
        }

        return size;
    }

    internal static string RoundName(int round, int rounds) => (rounds - round) switch
    {
        0 => "Finale",
        1 => "Halbfinale",
        2 => "Viertelfinale",
        3 => "Achtelfinale",
        _ => $"Runde {round}",
    };

    private static string Label(int round, int rounds, int position, int matchesInRound)
    {
        var name = RoundName(round, rounds);
        return matchesInRound == 1 ? name : $"{name} {position}";
    }
}

/// <summary>
/// Jeder gegen jeden nach dem Kreisverfahren: bei n Teilnehmern n-1 Runden
/// (bei ungeradem n setzt je Runde einer aus), jede Paarung genau einmal.
/// </summary>
internal static class RoundRobinDraw
{
    public static List<Match> Build(IReadOnlyList<Participant> order)
    {
        var ring = order.Select(p => (Guid?)p.Id).ToList();

        if (ring.Count % 2 == 1)
        {
            ring.Add(null);
        }

        var n = ring.Count;
        var matches = new List<Match>();

        for (var round = 1; round < n; round++)
        {
            var position = 1;

            for (var i = 0; i < n / 2; i++)
            {
                var a = ring[i];
                var b = ring[n - 1 - i];

                if (a is null || b is null)
                {
                    continue;
                }

                // Heimrecht wechseln, damit niemand immer auf derselben Seite steht.
                var (side1, side2) = (round + i) % 2 == 0 ? (a.Value, b.Value) : (b.Value, a.Value);

                matches.Add(new Match(
                    Guid.NewGuid(), round, position++, $"Runde {round}",
                    Side.Of(side1), Side.Of(side2)));
            }

            // Drehen: der erste bleibt, alle anderen rücken um eins weiter.
            var last = ring[n - 1];
            ring.RemoveAt(n - 1);
            ring.Insert(1, last);
        }

        return matches;
    }
}
