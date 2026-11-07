using System;
using System.Collections.Generic;
using System.Linq;

namespace GlamourTagger.Windows;

// Filtering of the item table: the Item Name search box and the list rebuild.
// TODO C1: move the rest of the filter code here from MainWindow.Table.cs (filter state fields,
// ResetAllFilters, the filter parts of the header popups) so all filtering lives in one file.
public partial class MainWindow
{
    // =============================================================================================
    // Item Name search box
    //   term:      optional prefix + text, matched with "contains" (case-insensitive)
    //              (none) = item name, t:/tag: = tags, p: = Penumbra mods, j:/job: = jobs,
    //              id: = item id, m:/model: = model id
    //   combining: ';' = AND, '|' or a standalone word "or" = OR, '-' in front = NOT, ( ) = group
    //              ';' binds tighter than OR:  a; b or c  =  (a; b) or c
    //              a prefix right before ( ) applies to the terms inside that have none (nested groups too):
    //              t:(long | demi) = t:long | t:demi,  and  -t:(long | demi) = neither tag (NOT of the whole group)
    //   Never fails while typing: empty terms, a prefix without text, dangling operators and
    //   unbalanced parentheses are skipped.
    // =============================================================================================

    private enum SearchField { Name, Tag, Mod, Job, Id, Model }

    // one node of a parsed search: a single term (Children == null) or an AND / OR group
    private sealed class SearchNode
    {
        public SearchField Field;
        public string Text = string.Empty;
        public bool Negated;               // '-' in front
        public bool IsOr;                  // groups only
        public List<SearchNode>? Children;
    }

    // the parsed search box, reused until nameFilter changes (the other filters rebuild the list too)
    private string? parsedSearchText;
    private SearchNode? parsedSearch;      // null = no usable term
    private bool parsedSearchUsesMods;
    private bool parsedSearchUsesTags;

    private void UpdateParsedSearch()
    {
        if (parsedSearchText == nameFilter) return;

        parsedSearchText = nameFilter;
        parsedSearch = SearchParser.Parse(nameFilter);
        parsedSearchUsesMods = parsedSearch != null && UsesSearchField(parsedSearch, SearchField.Mod);
        parsedSearchUsesTags = parsedSearch != null && UsesSearchField(parsedSearch, SearchField.Tag);
    }

    private static bool UsesSearchField(SearchNode node, SearchField field)
    {
        if (node.Children == null) return node.Field == field;

        foreach (var child in node.Children)
        {
            if (UsesSearchField(child, field)) return true;
        }
        return false;
    }

    // AND groups start true and stop at the first child that fails, OR groups start false and stop at the first match
    private static bool MatchesSearch(SearchNode node, ItemRow item, List<string>? tags, List<string>? mods)
    {
        bool matches;
        if (node.Children == null)
        {
            matches = MatchesSearchTerm(node, item, tags, mods);
        }
        else
        {
            matches = !node.IsOr;
            foreach (var child in node.Children)
            {
                if (MatchesSearch(child, item, tags, mods) == node.IsOr)
                {
                    matches = node.IsOr;
                    break;
                }
            }
        }
        return matches != node.Negated;
    }

    private static bool MatchesSearchTerm(SearchNode term, ItemRow item, List<string>? tags, List<string>? mods)
    {
        string text = term.Text;
        return term.Field switch
        {
            SearchField.Tag => AnyContains(tags, text),
            // 'p:' szűrés: csak akkor felel meg, ha a mod neve tartalmazza a keresett szöveget
            SearchField.Mod => AnyContains(mods, text),
            SearchField.Job => item.JobString.Contains(text, StringComparison.OrdinalIgnoreCase) || AnyContains(item.JobAbbrs, text),
            SearchField.Id => item.IdString.Contains(text, StringComparison.OrdinalIgnoreCase),
            SearchField.Model => item.ModelString.Contains(text, StringComparison.OrdinalIgnoreCase),
            _ => item.Name.Contains(text, StringComparison.OrdinalIgnoreCase),
        };
    }

    // plain loops instead of LINQ: these run for every item on every rebuild
    private static bool AnyContains(List<string>? values, string text)
    {
        if (values == null) return false;

        foreach (var value in values)
        {
            if (value.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool AnyContains(HashSet<string> values, string text)
    {
        foreach (var value in values)
        {
            if (value.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // Current-job button (MainWindow.Table.cs): adds "j:ABBR" to the search with AND, a second click takes it out.
    // Other terms are left alone. With an OR at the top level the search is put in parentheses first,
    // otherwise the job would only narrow the last OR branch (';' binds tighter than OR).
    private static string ToggleJobSearchTerm(string search, string jobAbbr)
    {
        string text = search.Trim();
        string term = $"j:{jobAbbr}";

        if (SearchParser.HasTopLevelOr(text)) return $"({text}); {term}";

        var parts = SplitTopLevelAnd(text);
        if (parts.RemoveAll(part => IsJobTerm(part, jobAbbr)) == 0)
        {
            return text.Length == 0 ? term : $"{text.TrimEnd(';', ' ')}; {term}";
        }

        // only the parenthesised OR search is left (the button wrapped it earlier): unwrap it
        if (parts.Count == 1 && IsWrappedOrGroup(parts[0])) return parts[0][1..^1].Trim();
        return string.Join("; ", parts);
    }

    private static bool IsJobTerm(string part, string jobAbbr) =>
        SearchParser.ParseTerm(part) is { Field: SearchField.Job, Negated: false } term
        && term.Text.Equals(jobAbbr, StringComparison.OrdinalIgnoreCase);

    // splits on the ';' that are outside every parenthesis; empty parts are dropped
    private static List<string> SplitTopLevelAnd(string text)
    {
        var parts = new List<string>();
        int level = 0;
        int start = 0;

        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length)
            {
                char c = text[i];
                if (c == '(') level++;
                else if (c == ')' && level > 0) level--;
                if (c != ';' || level > 0) continue;
            }

            string part = text[start..i].Trim();
            if (part.Length > 0) parts.Add(part);
            start = i + 1;
        }
        return parts;
    }

    // "( ... )" as one group, with an OR at its own top level
    private static bool IsWrappedOrGroup(string part)
    {
        if (part.Length < 2 || part[0] != '(' || part[^1] != ')') return false;

        int level = 0;
        for (int i = 0; i < part.Length; i++)
        {
            if (part[i] == '(') level++;
            else if (part[i] == ')') level--;
            if (level == 0 && i < part.Length - 1) return false; // the first '(' closes before the end
        }
        return SearchParser.HasTopLevelOr(part[1..^1]);
    }

    // search text -> SearchNodes: tokens first, then a small recursive descent parser
    private sealed class SearchParser
    {
        private enum TokenKind { Text, And, Or, Not, Prefix, Open, Close, End }
        private readonly record struct Token(TokenKind Kind, string Text = "", SearchField Field = SearchField.Name);

        private readonly List<Token> tokens;
        private int position;
        private int depth;
        // field of the terms without a prefix: the item name, or the prefix of the group around them
        private SearchField defaultField = SearchField.Name;

        private SearchParser(List<Token> tokens) => this.tokens = tokens;

        public static SearchNode? Parse(string input) => new SearchParser(Tokenize(input)).ParseOr();

        // true if the search has an OR outside of every parenthesis
        public static bool HasTopLevelOr(string input)
        {
            int level = 0;
            foreach (var token in Tokenize(input))
            {
                if (token.Kind == TokenKind.Open) level++;
                else if (token.Kind == TokenKind.Close && level > 0) level--;
                else if (token.Kind == TokenKind.Or && level == 0) return true;
            }
            return false;
        }

        // "-t: gothic" -> NOT (tag contains "gothic"). No known prefix = the default field (item name, or the group's prefix).
        public static SearchNode? ParseTerm(string raw, SearchField defaultField = SearchField.Name)
        {
            string text = raw.Trim();
            bool negated = false;
            if (text.StartsWith('-'))
            {
                negated = true;
                text = text[1..].TrimStart();
            }

            var field = defaultField;
            int colon = text.IndexOf(':');
            if (colon > 0 && TryGetPrefixField(text[..colon], out var prefixField))
            {
                field = prefixField;
                text = text[(colon + 1)..].Trim();
            }

            return text.Length == 0 ? null : new SearchNode { Field = field, Text = text, Negated = negated };
        }

        // the known search prefixes, without the ':'
        private static bool TryGetPrefixField(string prefix, out SearchField field)
        {
            SearchField? known = prefix.Trim().ToLowerInvariant() switch
            {
                "t" or "tag" => SearchField.Tag,
                "p" => SearchField.Mod,
                "j" or "job" => SearchField.Job,
                "id" => SearchField.Id,
                "m" or "model" => SearchField.Model,
                _ => null,
            };

            field = known ?? SearchField.Name;
            return known != null;
        }

        // ';' '|' '(' ')' are always operators. Between them a standalone word "or" is OR; right before '('
        // a lone '-' is NOT and a known prefix ("t:") becomes the prefix of that group.
        private static List<Token> Tokenize(string input)
        {
            var result = new List<Token>();
            int runStart = 0;

            for (int i = 0; i < input.Length; i++)
            {
                TokenKind? kind = input[i] switch
                {
                    ';' => TokenKind.And,
                    '|' => TokenKind.Or,
                    '(' => TokenKind.Open,
                    ')' => TokenKind.Close,
                    _ => null,
                };
                if (kind == null) continue;

                AddTextRun(result, input[runStart..i], beforeOpen: kind == TokenKind.Open);
                result.Add(new Token(kind.Value));
                runStart = i + 1;
            }

            AddTextRun(result, input[runStart..], beforeOpen: false);
            return result;
        }

        private static void AddTextRun(List<Token> result, string run, bool beforeOpen)
        {
            string[] words = run.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            int end = words.Length;

            // what stands right before '(' belongs to the group: "-" = NOT, "t:" = the group's prefix, "-t:" / "- t:" = both
            bool negatesGroup = false;
            SearchField? groupField = null;
            if (beforeOpen && end > 0)
            {
                string last = words[end - 1];
                if (last == "-")
                {
                    negatesGroup = true;
                    end--;
                }
                else
                {
                    bool minus = last.StartsWith('-');
                    string prefix = minus ? last[1..] : last;
                    if (prefix.EndsWith(':') && TryGetPrefixField(prefix[..^1], out var field))
                    {
                        groupField = field;
                        negatesGroup = minus;
                        end--;

                        if (!minus && end > 0 && words[end - 1] == "-")
                        {
                            negatesGroup = true;
                            end--;
                        }
                    }
                }
            }

            int textStart = 0;
            for (int w = 0; w < end; w++)
            {
                if (!words[w].Equals("or", StringComparison.OrdinalIgnoreCase)) continue;

                AddText(result, words, textStart, w);
                result.Add(new Token(TokenKind.Or));
                textStart = w + 1;
            }
            AddText(result, words, textStart, end);

            if (negatesGroup) result.Add(new Token(TokenKind.Not));
            if (groupField != null) result.Add(new Token(TokenKind.Prefix, Field: groupField.Value));
        }

        private static void AddText(List<Token> result, string[] words, int start, int end)
        {
            if (end > start) result.Add(new Token(TokenKind.Text, string.Join(' ', words, start, end - start)));
        }

        private TokenKind Peek => position < tokens.Count ? tokens[position].Kind : TokenKind.End;

        // AND groups joined by OR
        private SearchNode? ParseOr()
        {
            var parts = new List<SearchNode>();
            while (true)
            {
                var part = ParseAnd();
                if (part != null) parts.Add(part);

                if (Peek != TokenKind.Or) break;
                position++;
            }
            return Combine(parts, isOr: true);
        }

        // terms joined by ';' (or simply standing next to each other)
        private SearchNode? ParseAnd()
        {
            var parts = new List<SearchNode>();
            while (true)
            {
                var kind = Peek;
                if (kind is TokenKind.End or TokenKind.Or) break;

                if (kind == TokenKind.Close)
                {
                    if (depth > 0) break;
                    position++; // stray ')'
                    continue;
                }

                if (kind == TokenKind.And)
                {
                    position++;
                    continue;
                }

                var part = ParseUnary();
                if (part != null) parts.Add(part);
            }
            return Combine(parts, isOr: false);
        }

        // optional '-', then a term, a ( group ) or a prefixed t:( group )
        private SearchNode? ParseUnary()
        {
            bool negated = false;
            while (Peek == TokenKind.Not)
            {
                negated = !negated;
                position++;
            }

            SearchNode? node = null;
            if (Peek == TokenKind.Prefix)
            {
                // terms inside without their own prefix use this one; nested groups inherit it
                var outerField = defaultField;
                defaultField = tokens[position].Field;
                position++;
                node = ParseGroup();
                defaultField = outerField;
            }
            else if (Peek == TokenKind.Open)
            {
                node = ParseGroup();
            }
            else if (Peek == TokenKind.Text)
            {
                node = ParseTerm(tokens[position].Text, defaultField);
                position++;
            }

            if (node != null && negated) node.Negated = !node.Negated;
            return node;
        }

        // ( ... ): a missing ')' at the end is fine
        private SearchNode? ParseGroup()
        {
            if (Peek != TokenKind.Open) return null;

            position++;
            depth++;
            var node = ParseOr();
            depth--;
            if (Peek == TokenKind.Close) position++;
            return node;
        }

        private static SearchNode? Combine(List<SearchNode> parts, bool isOr) => parts.Count switch
        {
            0 => null,
            1 => parts[0],
            _ => new SearchNode { IsOr = isOr, Children = parts },
        };
    }

    // =============================================================================================
    // List rebuild
    // =============================================================================================

    // Applies every active filter to cachedItems, then dedup (one per model), sort and row striping.
    // Only runs when isDirty is set, not every frame.
    private void RebuildFilteredList()
    {
        UpdateParsedSearch();
        var search = parsedSearch;

        bool hasIdFilter = !string.IsNullOrEmpty(idFilter);
        bool hasModelFilter = !string.IsNullOrEmpty(modelFilter);
        bool allSlotsSelected = selectedSlots.Count == AvailableSlots.Length;
        bool allJobsSelected = selectedJobs.Count == AvailableJobs.Length;
        bool hasTagFilter = filterUntagged || selectedTags.Count > 0;

        // worked out once per rebuild, not per item: the Penumbra state, the mod names and the tags are
        // only looked up when a filter, the search or the PM sort needs them
        bool isModFiltered = selectedModFilters.Count < 4;
        bool needModState = isModFiltered || parsedSearchUsesMods;
        bool needTags = hasTagFilter || parsedSearchUsesTags;
        var requiredTags = selectedTags.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
        var excludedTags = selectedTags.Where(kv => kv.Value == -1).Select(kv => kv.Key).ToList();
        var sortModStates = sortColumnIndex == 5 ? new Dictionary<uint, int>() : null;

        var resultList = new List<ItemRow>(cachedItems.Count);

        plugin.PenumbraIpc.RefreshIfNeeded();

        foreach (var item in cachedItems)
        {
            List<string>? affectingMods = null;
            if (needModState)
            {
                // Lekérjük, hogy az adott tárgy moddolva van-e
                int modState = GetItemModState(item.Id, item.Name, out var actMods, out var demMods, out var unusableMods);
                if (sortModStates != null) sortModStates[item.Id] = modState;

                // Modded Filter (M oszlop) ellenőrzése (Mode 1 sees Demodded(1) & Modded(2). Mode 2 sees Unmodded(0))
                if (isModFiltered && !selectedModFilters.Contains(modState)) continue;

                // p: searches the mod names plus the demodded / unusable marks
                if (parsedSearchUsesMods)
                {
                    affectingMods = new List<string>(actMods.Count + unusableMods.Count + demMods.Count + 4);
                    affectingMods.AddRange(actMods);
                    affectingMods.AddRange(unusableMods);
                    affectingMods.AddRange(demMods);

                    if (unusableMods.Count > 0)
                    {
                        affectingMods.Add("unusable");
                        affectingMods.Add("[unusable]");
                    }

                    if (demMods.Count > 0)
                    {
                        affectingMods.Add("demodded");
                        affectingMods.Add("[demodded]");
                    }
                }
            }

            // flag columns
            if (!CheckFavState(plugin.Configuration.Favorites, item.Id, favFilterMode)) continue;
            if (!CheckFavState(plugin.Configuration.DoubleFavorites, item.Id, dfFilterMode)) continue;
            if (!CheckFavState(plugin.Configuration.TripleFavorites, item.Id, tfFilterMode)) continue;
            if (!CheckFavState(plugin.Configuration.Wishlist, item.Id, wlFilterMode)) continue;
            if (!CheckFavState(plugin.Configuration.DoubleWishlist, item.Id, dwlFilterMode)) continue;

            List<string>? itemTags = null;
            if (needTags) plugin.Configuration.ItemTags.TryGetValue(item.Id, out itemTags);
            bool hasItemTags = itemTags != null && itemTags.Count > 0;

            // Item Name search box (every term, see the top of this file)
            if (search != null && !MatchesSearch(search, item, itemTags, affectingMods)) continue;

            // the separate ID / Model search boxes, then the slot / job / dye filters
            if (hasIdFilter && !item.IdString.Contains(idFilter)) continue;
            if (hasModelFilter && !item.ModelString.Contains(modelFilter, StringComparison.OrdinalIgnoreCase)) continue;

            if (!allSlotsSelected && selectedSlots.Count > 0 && !selectedSlots.Contains(item.SlotName)) continue;
            if (!allJobsSelected && selectedJobs.Count > 0 && !item.JobAbbrs.Overlaps(selectedJobs)) continue;
            if (selectedDyes.Count > 0 && !selectedDyes.Contains(item.DyeCount)) continue;

            if (hasTagFilter)
            {
                bool matchUntagged = filterUntagged && !hasItemTags;

                bool matchSpecificTag = false;
                if (selectedTags.Count > 0)
                {
                    // 1. Pozitív tagek vizsgálata (AND vs OR mód szerint)
                    bool matchesRequired;
                    if (requiredTags.Count == 0)
                    {
                        matchesRequired = true; // Ha nincs pozitív tag kijelölve, minden item átmegy ezen a lépésen
                    }
                    else if (tagFilterMode == 1) // AND mód: ÖSSZES kijelölt pozitív tag kell
                    {
                        matchesRequired = hasItemTags && requiredTags.All(t => itemTags!.Contains(t));
                    }
                    else // OR mód: LEGALÁBB EGY kijelölt pozitív tag kell
                    {
                        matchesRequired = hasItemTags && requiredTags.Any(t => itemTags!.Contains(t));
                    }

                    // 2. Kizárt (NOT) tagek vizsgálata: EGYETLEN kizárt tag sem lehet rajta
                    bool matchesExcluded = !hasItemTags || !excludedTags.Any(t => itemTags!.Contains(t));

                    // 3. A kizárás MINDIG AND logikával szűkíti a pozitív találatokat:
                    matchSpecificTag = matchesRequired && matchesExcluded;
                }

                if (!matchUntagged && !matchSpecificTag)
                    continue;
            }

            resultList.Add(item);
        }

        // keep only the first item of each model (optionally ignoring the variant)
        if (onlyOnePerModelType)
        {
            var deduplicated = new List<ItemRow>(resultList.Count);
            if (ignoreVariants)
            {
                var seen = new HashSet<ushort>();
                foreach (var item in resultList)
                {
                    if (seen.Add(item.ModelMain)) deduplicated.Add(item);
                }
            }
            else
            {
                var seen = new HashSet<(ushort, ushort)>();
                foreach (var item in resultList)
                {
                    if (seen.Add((item.ModelMain, item.ModelVariant))) deduplicated.Add(item);
                }
            }
            resultList = deduplicated;
        }

        // sort by the clicked column, ties fall back to the name
        if (sortColumnIndex >= 0)
        {
            int col = sortColumnIndex;
            bool isAsc = sortAscending;

            // PM column: each item's mod state is looked up once, not on every comparison
            int ModStateOf(ItemRow row)
            {
                if (!sortModStates!.TryGetValue(row.Id, out int state))
                {
                    state = GetItemModState(row.Id, row.Name, out _, out _, out _);
                    sortModStates[row.Id] = state;
                }
                return state;
            }

            resultList.Sort((x, y) =>
            {
                int cmp = col switch
                {
                    0 => plugin.Configuration.Favorites.Contains(x.Id).CompareTo(plugin.Configuration.Favorites.Contains(y.Id)),
                    1 => plugin.Configuration.DoubleFavorites.Contains(x.Id).CompareTo(plugin.Configuration.DoubleFavorites.Contains(y.Id)),
                    2 => plugin.Configuration.TripleFavorites.Contains(x.Id).CompareTo(plugin.Configuration.TripleFavorites.Contains(y.Id)),
                    3 => plugin.Configuration.Wishlist.Contains(x.Id).CompareTo(plugin.Configuration.Wishlist.Contains(y.Id)),
                    4 => plugin.Configuration.DoubleWishlist.Contains(x.Id).CompareTo(plugin.Configuration.DoubleWishlist.Contains(y.Id)),
                    5 => ModStateOf(x).CompareTo(ModStateOf(y)),
                    6 => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase),
                    7 => x.Id.CompareTo(y.Id),
                    8 => x.ModelMain != y.ModelMain ? x.ModelMain.CompareTo(y.ModelMain) : x.ModelVariant.CompareTo(y.ModelVariant),
                    9 => string.Compare(x.JobString, y.JobString, StringComparison.OrdinalIgnoreCase),
                    10 => x.DyeCount.CompareTo(y.DyeCount),
                    11 => CompareItemTags(x.Id, y.Id, isAsc),
                    _ => 0
                };

                if (cmp == 0 && col != 6) cmp = string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
                return isAsc ? cmp : -cmp;
            });
        }

        filteredItems = resultList;

        // row striping: the stripe flips when the model id changes, so items sharing a model sit in one band
        filteredItemToggles.Clear();
        if (filteredItems.Count > 0)
        {
            bool currentToggle = true;
            ushort lastModelMain = filteredItems[0].ModelMain;
            filteredItemToggles.Add(currentToggle);

            for (int x = 1; x < filteredItems.Count; x++)
            {
                var item = filteredItems[x];
                if (item.ModelMain != lastModelMain)
                {
                    currentToggle = !currentToggle;
                    lastModelMain = item.ModelMain;
                }
                filteredItemToggles.Add(currentToggle);
            }
        }
    }

    private int CompareItemTags(uint xId, uint yId, bool isAsc)
    {
        bool hasX = plugin.Configuration.ItemTags.TryGetValue(xId, out var tagsX) && tagsX.Count > 0;
        bool hasY = plugin.Configuration.ItemTags.TryGetValue(yId, out var tagsY) && tagsY.Count > 0;

        // 1. Ha az egyiknek van tagje, a másiknak nincs: a tagged elem MINDIG elöl marad
        if (hasX && !hasY) return isAsc ? -1 : 1;
        if (!hasX && hasY) return isAsc ? 1 : -1;

        // 2. Ha egyiknek sincs tagje: egyenlőek
        if (!hasX && !hasY) return 0;

        // 3. Ha mindkettőnek van tagje: ábécé szerinti rendezés a tag neve alapján
        return string.Compare(tagsX![0], tagsY![0], StringComparison.OrdinalIgnoreCase);
    }

    // mode 0 = all, 1 = only flagged, 2 = only unflagged
    private bool CheckFavState(HashSet<uint> set, uint itemId, int mode)
    {
        bool isFav = set.Contains(itemId);
        if (mode == 1 && !isFav) return false;
        if (mode == 2 && isFav) return false;
        return true;
    }
}