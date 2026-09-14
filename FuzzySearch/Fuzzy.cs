// Copyright (c) 2023-2026 ktsu-dev contributors

using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("ktsu.FuzzySearch.Test")]

namespace ktsu.FuzzySearch;

// Adapted from: https://gist.github.com/CDillinger/2aa02128f840bdca90340ce08ee71bc2

/// <summary>
/// Provides fuzzy string matching capabilities, allowing for approximate string matching and scoring.
/// </summary>
/// <remarks>
/// <para>
/// This implementation uses a scoring system that rewards consecutive matches, matches after separator characters,
/// and matches across camelCase boundaries, while penalizing unmatched characters.
/// </para>
/// <para>
/// Input is normalized to <see cref="NormalizationForm.FormC"/> before matching, so canonically equivalent text
/// matches whether it is precomposed (NFC) or decomposed (NFD). Normalization relies on the runtime's globalization
/// data: in an application running with invariant globalization enabled, <see cref="string.Normalize(NormalizationForm)"/>
/// is a no-op and the two forms of the same text will not match.
/// </para>
/// <para>
/// Matching advances one Unicode codepoint at a time rather than one UTF-16 code unit at a time, so a surrogate pair
/// is matched as a whole and an unpaired surrogate cannot match half of an unrelated supplementary-plane character.
/// </para>
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
	/// <remarks>
	/// <para>
	/// Subject and pattern are normalized to <see cref="NormalizationForm.FormC"/> before comparison, so canonically
	/// equivalent text matches regardless of whether it is precomposed (NFC) or decomposed (NFD).
	/// </para>
	/// <para>
	/// Comparison advances one Unicode codepoint at a time, so a surrogate pair matches only as a whole and a lone
	/// surrogate can never match half of an unrelated supplementary-plane character.
	/// </para>
	/// </remarks>
	public static bool Contains(ReadOnlySpan<char> subject, ReadOnlySpan<char> pattern) =>
		ContainsCore(NormalizeForComparison(subject), NormalizeForComparison(pattern));

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
	/// Determines whether the specified subject contains all characters from the pattern in sequence, assuming both
	/// spans are already normalized to a common form.
	/// </summary>
	/// <param name="subject">The span of characters to search within.</param>
	/// <param name="pattern">The sequence of characters to search for.</param>
	/// <returns>
	/// <c>true</c> if the subject contains all characters from the pattern in sequence, or the pattern is empty and the subject is not; otherwise, <c>false</c>.
	/// </returns>
	internal static bool ContainsCore(ReadOnlySpan<char> subject, ReadOnlySpan<char> pattern)
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
			int patternCharLength = CodepointLengthAt(pattern, patternIdx);
			int strCharLength = CodepointLengthAt(subject, strIdx);

			if (CodepointsEqual(pattern, patternIdx, patternCharLength, subject, strIdx, strCharLength))
			{
				patternIdx += patternCharLength;
			}

			strIdx += strCharLength;
		}

		return patternIdx == patternLength;
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
	/// <remarks>
	/// <para>
	/// Subject and pattern are normalized to <see cref="NormalizationForm.FormC"/> before comparison, so canonically
	/// equivalent text matches regardless of whether it is precomposed (NFC) or decomposed (NFD).
	/// </para>
	/// <para>
	/// Scoring advances one Unicode codepoint at a time, so a surrogate pair is scored as a single character and an
	/// unpaired surrogate cannot match half of an unrelated supplementary-plane character.
	/// </para>
	/// </remarks>
	internal static int CalculateScore(ReadOnlySpan<char> subject, ReadOnlySpan<char> pattern, out bool wholePatternIsPresent) =>
		CalculateScoreCore(NormalizeForComparison(subject), NormalizeForComparison(pattern), out wholePatternIsPresent);

	/// <summary>
	/// Calculates a fuzzy match score between the subject span and pattern span, assuming both spans are already
	/// normalized to a common form.
	/// </summary>
	/// <param name="subject">The span of characters to search within.</param>
	/// <param name="pattern">The sequence of characters to search for.</param>
	/// <param name="wholePatternIsPresent">
	/// When this method returns, contains <c>true</c> if the entire pattern was found in the subject,
	/// or the pattern is empty and the subject is not; otherwise, <c>false</c>.
	/// </param>
	/// <returns>A score representing the quality of the match. Higher scores indicate better matches.</returns>
	internal static int CalculateScoreCore(ReadOnlySpan<char> subject, ReadOnlySpan<char> pattern, out bool wholePatternIsPresent)
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

		// Use "best" matched codepoint if multiple subject codepoints match the pattern
		int? bestLetterIdx = null;
		int bestLetterLength = 0;
		int bestLetterScore = 0;

		// Loop over codepoints in subject
		while (strIdx != strLength)
		{
			bool hasPatternChar = patternIdx != patternLength;
			int patternCharLength = hasPatternChar ? CodepointLengthAt(pattern, patternIdx) : 0;
			int strCharLength = CodepointLengthAt(subject, strIdx);

			// The leading code unit carries the case and separator properties of the codepoint: a surrogate is
			// caseless and is never a separator, which is exactly how a supplementary-plane codepoint should behave.
			char strChar = subject[strIdx];
			char strLower = char.ToLowerInvariant(strChar);
			char strUpper = char.ToUpperInvariant(strChar);

			bool nextMatch = hasPatternChar && CodepointsEqual(pattern, patternIdx, patternCharLength, subject, strIdx, strCharLength);
			bool rematch = bestLetterIdx is not null && CodepointsEqual(subject, bestLetterIdx.Value, bestLetterLength, subject, strIdx, strCharLength);

			bool advanced = nextMatch && bestLetterIdx is not null;
			bool patternRepeat = bestLetterIdx is not null && hasPatternChar && CodepointsEqual(subject, bestLetterIdx.Value, bestLetterLength, pattern, patternIdx, patternCharLength);
			if (bestLetterIdx is not null && (advanced || patternRepeat))
			{
				score += bestLetterScore;
				bestLetterIdx = null;
				bestLetterLength = 0;
				bestLetterScore = 0;
			}

			if (nextMatch || rematch)
			{
				int newScore = 0;

				score = PenalizeNonPatternCharacters(score, patternIdx, strIdx);

				newScore = ApplyBonuses(prevMatched, prevLower, prevSeparator, strChar, strLower, strUpper, newScore);

				// Update pattern index IF the next pattern codepoint was matched
				if (nextMatch)
				{
					patternIdx += patternCharLength;
				}

				// Update best letter in stringToSearch which may be for a "next" letter or a "rematch"
				if (newScore >= bestLetterScore)
				{
					// Apply penalty for now skipped letter
					if (bestLetterIdx is not null)
					{
						score += unmatchedLetterPenalty;
					}

					bestLetterIdx = strIdx;
					bestLetterLength = strCharLength;
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

			strIdx += strCharLength;
		}

		// Apply score for last match
		if (bestLetterIdx is not null)
		{
			score += bestLetterScore;
		}

		wholePatternIsPresent = patternIdx == patternLength;
		return score;
	}

	/// <summary>
	/// Gets the number of UTF-16 code units occupied by the Unicode codepoint starting at the given index.
	/// </summary>
	/// <param name="value">The span to inspect.</param>
	/// <param name="index">The index of the first code unit of the codepoint.</param>
	/// <returns>
	/// <c>2</c> when the index starts a well-formed surrogate pair; otherwise <c>1</c>. An unpaired surrogate is a
	/// codepoint of its own, so it is never treated as half of a neighbouring character.
	/// </returns>
	internal static int CodepointLengthAt(ReadOnlySpan<char> value, int index) =>
		index + 1 < value.Length && char.IsHighSurrogate(value[index]) && char.IsLowSurrogate(value[index + 1])
			? 2
			: 1;

	/// <summary>
	/// Determines whether the codepoints at the given indices are the same, ignoring case.
	/// </summary>
	/// <param name="left">The span holding the first codepoint.</param>
	/// <param name="leftIndex">The index of the first code unit of the codepoint in <paramref name="left"/>.</param>
	/// <param name="leftLength">The length in code units of the codepoint in <paramref name="left"/>.</param>
	/// <param name="right">The span holding the second codepoint.</param>
	/// <param name="rightIndex">The index of the first code unit of the codepoint in <paramref name="right"/>.</param>
	/// <param name="rightLength">The length in code units of the codepoint in <paramref name="right"/>.</param>
	/// <returns><c>true</c> if the two codepoints are equal ignoring case; otherwise, <c>false</c>.</returns>
	/// <remarks>
	/// Codepoints of differing code-unit length are never equal, which is what stops an unpaired surrogate from
	/// matching one half of an unrelated surrogate pair. Supplementary-plane codepoints are compared exactly:
	/// <see cref="char.ToLowerInvariant(char)"/> operates on single UTF-16 code units and has no case mapping to apply
	/// to a surrogate.
	/// </remarks>
	internal static bool CodepointsEqual(ReadOnlySpan<char> left, int leftIndex, int leftLength, ReadOnlySpan<char> right, int rightIndex, int rightLength)
	{
		if (leftLength != rightLength)
		{
			return false;
		}

		return leftLength == 2
			? left[leftIndex] == right[rightIndex] && left[leftIndex + 1] == right[rightIndex + 1]
			: char.ToLowerInvariant(left[leftIndex]) == char.ToLowerInvariant(right[rightIndex]);
	}

	/// <summary>
	/// Normalizes a span to <see cref="NormalizationForm.FormC"/> so that canonically equivalent text compares equal.
	/// </summary>
	/// <param name="value">The span to normalize.</param>
	/// <returns>
	/// The normalized text, or <paramref name="value"/> itself when it is already in that form or cannot be normalized.
	/// </returns>
	/// <remarks>
	/// ASCII-only text is always already in <see cref="NormalizationForm.FormC"/>, so the common case is served by a
	/// scan that allocates nothing. Only text containing a non-ASCII character pays for the conversion.
	/// </remarks>
	internal static ReadOnlySpan<char> NormalizeForComparison(ReadOnlySpan<char> value)
	{
		if (IsAscii(value))
		{
			return value;
		}

		try
		{
			return value.ToString().Normalize(NormalizationForm.FormC).AsSpan();
		}
		catch (ArgumentException)
		{
			// Text that is not well-formed Unicode (for example a lone surrogate) cannot be normalized.
			// Compare it as it was given rather than failing the match outright.
			return value;
		}
	}

	/// <summary>
	/// Determines whether every character in the span is an ASCII character.
	/// </summary>
	/// <param name="value">The span to inspect.</param>
	/// <returns><c>true</c> if the span contains only ASCII characters; otherwise, <c>false</c>.</returns>
	internal static bool IsAscii(ReadOnlySpan<char> value)
	{
		foreach (char c in value)
		{
			if (c > 0x7F)
			{
				return false;
			}
		}

		return true;
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
