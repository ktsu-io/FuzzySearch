// Copyright (c) 2023-2026 ktsu-dev contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ktsu.FuzzySearch.Test")]

namespace ktsu.FuzzySearch;

// Adapted from: https://gist.github.com/CDillinger/2aa02128f840bdca90340ce08ee71bc2

/// <summary>
/// Provides fuzzy string matching capabilities, allowing for approximate string matching and scoring.
/// </summary>
/// <remarks>
/// This implementation uses a scoring system that rewards consecutive matches, matches after separator characters,
/// and matches across camelCase boundaries, while penalizing unmatched characters.
/// </remarks>
public static class Fuzzy
{
	/// <summary>The bonus score awarded for adjacent character matches.</summary>
	internal const int adjacentMatchBonus = 5;

	/// <summary>The bonus score awarded for matches that occur after a separator character ('_' or space).</summary>
	internal const int matchAfterSeparatorBonus = 10;

	/// <summary>The bonus score awarded for matches that occur at camelCase boundaries.</summary>
	internal const int camelCaseMatchBonus = 10;

	/// <summary>The penalty for each unmatched character at the beginning of the string.</summary>
	internal const int unmatchedPrefixLetterPenalty = -1;

	/// <summary>The maximum prefix penalty that can be applied.</summary>
	internal const int maxPrefixPenalty = -5;

	/// <summary>The penalty for each unmatched character in the string.</summary>
	internal const int unmatchedLetterPenalty = -1;

	/// <summary>
	/// Determines whether the specified subject contains all characters from the pattern in sequence.
	/// </summary>
	/// <param name="subject">The span of characters to search within.</param>
	/// <param name="pattern">The sequence of characters to search for.</param>
	/// <returns>
	/// <c>true</c> if the subject contains all characters from the pattern in sequence, or the pattern is empty and the subject is not; otherwise, <c>false</c>.
	/// </returns>
	public static bool Contains(ReadOnlySpan<char> subject, ReadOnlySpan<char> pattern)
	{
		if (pattern.IsEmpty)
		{
			return !subject.IsEmpty;
		}

		int patternIdx = 0;
		int strIdx = 0;
		int patternLength = pattern.Length;
		int strLength = subject.Length;

		while (patternIdx != patternLength && strIdx != strLength)
		{
			if (char.ToLowerInvariant(pattern[patternIdx]) == char.ToLowerInvariant(subject[strIdx]))
			{
				++patternIdx;
			}

			++strIdx;
		}

		return patternIdx == patternLength;
	}

	/// <summary>
	/// Determines whether the specified subject contains all characters from the pattern in sequence, and calculates a match score.
	/// </summary>
	/// <param name="subject">The span of characters to search within.</param>
	/// <param name="pattern">The sequence of characters to search for.</param>
	/// <param name="outScore">
	/// When this method returns, contains the calculated match score if the pattern is found; otherwise,
	/// the score reflects how close the match was.
	/// </param>
	/// <returns>
	/// <c>true</c> if the subject contains all characters from the pattern in sequence, or the pattern is empty and the subject is not; otherwise, <c>false</c>.
	/// </returns>
	public static bool Contains(ReadOnlySpan<char> subject, ReadOnlySpan<char> pattern, out int outScore)
	{
		outScore = CalculateScore(subject, pattern, out bool wholePatternPresent);
		return wholePatternPresent;
	}

	/// <summary>
	/// Calculates a fuzzy match score between the subject span and pattern span.
	/// </summary>
	/// <param name="subject">The span of characters to search within.</param>
	/// <param name="pattern">The sequence of characters to search for.</param>
	/// <param name="wholePatternIsPresent">
	/// When this method returns, contains <c>true</c> if the entire pattern was found in the subject,
	/// or the pattern is empty and the subject is not; otherwise, <c>false</c>.
	/// </param>
	/// <returns>A score representing the quality of the match. Higher scores indicate better matches.</returns>
	internal static int CalculateScore(ReadOnlySpan<char> subject, ReadOnlySpan<char> pattern, out bool wholePatternIsPresent)
	{
		if (pattern.IsEmpty)
		{
			wholePatternIsPresent = !subject.IsEmpty;
			return 0;
		}

		int score = 0;
		int patternIdx = 0;
		int patternLength = pattern.Length;
		int strIdx = 0;
		int strLength = subject.Length;
		bool prevMatched = false;
		bool prevLower = false;
		bool prevSeparator = true; // true if first letter match gets separator bonus

		// Use "best" matched letter if multiple string letters match the pattern
		char? bestLetter = null;
		char? bestLower = null;
		int? bestLetterIdx = null;
		int bestLetterScore = 0;

		// Loop over characters in subject
		while (strIdx != strLength)
		{
			char? patternChar = patternIdx != patternLength ? pattern[patternIdx] : null;
			char strChar = subject[strIdx];

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
				bestLetter = null;
				bestLower = null;
				bestLetterIdx = null;
				bestLetterScore = 0;
			}

			if (nextMatch || rematch)
			{
				int newScore = 0;

				score = PenalizeNonPatternCharacters(score, patternIdx, strIdx);

				newScore = ApplyBonuses(prevMatched, prevLower, prevSeparator, strChar, strLower, strUpper, newScore);

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
					bestLower = char.ToLowerInvariant(strChar);
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

			// "clever" isLetter check.
			bool isLetter = strLower != strUpper;

			prevLower = strChar == strLower && isLetter;
			prevSeparator = strChar is '_' or ' ';

			++strIdx;
		}

		// Apply score for last match
		if (bestLetter is not null && bestLetterIdx is not null)
		{
			score += bestLetterScore;
		}

		wholePatternIsPresent = patternIdx == patternLength;
		return score;
	}

	/// <summary>
	/// Applies bonus scores for various match characteristics.
	/// </summary>
	/// <param name="prevMatched">Whether the previous character was a match.</param>
	/// <param name="prevLower">Whether the previous character was lowercase.</param>
	/// <param name="prevSeparator">Whether the previous character was a separator.</param>
	/// <param name="strChar">The current character being considered.</param>
	/// <param name="strLower">The lowercase form of the current character.</param>
	/// <param name="strUpper">The uppercase form of the current character.</param>
	/// <param name="newScore">The current score to apply bonuses to.</param>
	/// <returns>The updated score after applying any applicable bonuses.</returns>
	internal static int ApplyBonuses(bool prevMatched, bool prevLower, bool prevSeparator, char strChar, char strLower, char strUpper, int newScore)
	{
		// Apply bonus for consecutive bonuses
		if (prevMatched)
		{
			newScore += adjacentMatchBonus;
		}

		// Apply bonus for matches after a separator
		if (prevSeparator)
		{
			newScore += matchAfterSeparatorBonus;
		}

		// Apply bonus across camel case boundaries. Includes "clever" isLetter check.
		if (prevLower && strChar == strUpper && strLower != strUpper)
		{
			newScore += camelCaseMatchBonus;
		}

		return newScore;
	}

	/// <summary>
	/// Applies penalties for characters that don't match the pattern.
	/// </summary>
	/// <param name="score">The current score to apply penalties to.</param>
	/// <param name="patternIdx">The current index in the pattern.</param>
	/// <param name="strIdx">The current index in the subject span.</param>
	/// <returns>The updated score after applying any applicable penalties.</returns>
	internal static int PenalizeNonPatternCharacters(int score, int patternIdx, int strIdx)
	{
		// Apply penalty for each letter before the first pattern match
		// Note: Math.Max because penalties are negative values. So max is smallest penalty.
		if (patternIdx == 0)
		{
			int penalty = Math.Max(strIdx * unmatchedPrefixLetterPenalty, maxPrefixPenalty);
			score += penalty;
		}

		return score;
	}
}
