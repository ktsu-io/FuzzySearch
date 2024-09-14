#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace ktsu.FuzzySearch;

public static class Fuzzy
{
	// Adapted from: https://gist.github.com/CDillinger/2aa02128f840bdca90340ce08ee71bc2
	public static bool Contains(string stringToSearch, string pattern)
	{
		ArgumentNullException.ThrowIfNull(stringToSearch, nameof(stringToSearch));
		ArgumentNullException.ThrowIfNull(pattern, nameof(pattern));

		int patternIdx = 0;
		int strIdx = 0;
		int patternLength = pattern.Length;
		int strLength = stringToSearch.Length;

		while (patternIdx != patternLength && strIdx != strLength)
		{
			if (char.ToLowerInvariant(pattern[patternIdx]) == char.ToLowerInvariant(stringToSearch[strIdx]))
			{
				++patternIdx;
			}

			++strIdx;
		}

		return patternLength != 0 && strLength != 0 && patternIdx == patternLength;
	}

	// Adapted from: https://gist.github.com/CDillinger/2aa02128f840bdca90340ce08ee71bc2
	public static bool Contains(string stringToSearch, string pattern, out int outScore)
	{
		ArgumentNullException.ThrowIfNull(stringToSearch, nameof(stringToSearch));
		ArgumentNullException.ThrowIfNull(pattern, nameof(pattern));

		// Score consts
		const int adjacencyBonus = 5;               // bonus for adjacent matches
		const int separatorBonus = 10;              // bonus if match occurs after a separator
		const int camelBonus = 10;                  // bonus if match is uppercase and prev is lower

		const int leadingLetterPenalty = 0;         // penalty applied for every letter in stringToSearch before the first match
		const int maxLeadingLetterPenalty = 0;      // maximum penalty for leading letters
		const int unmatchedLetterPenalty = -1;      // penalty for every letter that doesn't matter

		// Loop variables
		int score = 0;
		int patternIdx = 0;
		int patternLength = pattern.Length;
		int strIdx = 0;
		int strLength = stringToSearch.Length;
		bool prevMatched = false;
		bool prevLower = false;
		bool prevSeparator = true;                   // true if first letter match gets separator bonus

		// Use "best" matched letter if multiple string letters match the pattern
		char? bestLetter = null;
		char? bestLower = null;
		int? bestLetterIdx = null;
		int bestLetterScore = 0;

		var matchedIndices = new List<int>();

		// Loop over strings
		while (strIdx != strLength)
		{
			char? patternChar = patternIdx != patternLength ? pattern[patternIdx] : null;
			char strChar = stringToSearch[strIdx];

			char? patternLower = patternChar is not null ? char.ToLowerInvariant((char)patternChar) : null;
			char strLower = char.ToLowerInvariant(strChar);
			char strUpper = char.ToUpperInvariant(strChar);

			bool nextMatch = patternChar is not null && patternLower == strLower;
			bool rematch = bestLetter is not null && bestLower == strLower;

			bool advanced = nextMatch && bestLetter is not null;
			bool patternRepeat = bestLetter is not null && patternChar is not null && bestLower == patternLower;
			if (bestLetterIdx is not null && (advanced || patternRepeat))
			{
				score += bestLetterScore;
				matchedIndices.Add((int)bestLetterIdx);
				bestLetter = null;
				bestLower = null;
				bestLetterIdx = null;
				bestLetterScore = 0;
			}

			if (nextMatch || rematch)
			{
				int newScore = 0;

				// Apply penalty for each letter before the first pattern match
				// Note: Math.Max because penalties are negative values. So max is smallest penalty.
				if (patternIdx == 0)
				{
					int penalty = Math.Max(strIdx * leadingLetterPenalty, maxLeadingLetterPenalty);
					score += penalty;
				}

				// Apply bonus for consecutive bonuses
				if (prevMatched)
				{
					newScore += adjacencyBonus;
				}

				// Apply bonus for matches after a separator
				if (prevSeparator)
				{
					newScore += separatorBonus;
				}

				// Apply bonus across camel case boundaries. Includes "clever" isLetter check.
				if (prevLower && strChar == strUpper && strLower != strUpper)
				{
					newScore += camelBonus;
				}

				// Update pattern index IF the next pattern letter was matched
				if (nextMatch)
				{
					++patternIdx;
				}

				// Update best letter in stringToSearch which may be for a "next" letter or a "rematch"
				if (newScore >= bestLetterScore)
				{
					// Apply penalty for now skipped letter
					if (bestLetter is not null)
					{
						score += unmatchedLetterPenalty;
					}

					bestLetter = strChar;
					bestLower = char.ToLowerInvariant((char)bestLetter);
					bestLetterIdx = strIdx;
					bestLetterScore = newScore;
				}

				prevMatched = true;
			}
			else
			{
				score += unmatchedLetterPenalty;
				prevMatched = false;
			}

			// Includes "clever" isLetter check.
			prevLower = strChar == strLower && strLower != strUpper;
			prevSeparator = strChar is '_' or ' ';

			++strIdx;
		}

		// Apply score for last match
		if (bestLetter is not null && bestLetterIdx is not null)
		{
			score += bestLetterScore;
			matchedIndices.Add((int)bestLetterIdx);
		}

		outScore = score;
		return patternIdx == patternLength;
	}
}
