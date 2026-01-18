using Newtonsoft.Json;
using SmartBot.Database;
using SmartBot.Plugins.API;
using SmartBotAPI.Plugins.API.HSReplayArchetypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// Original designer of this plugin is Olanga and EE
// Last edit 17/December/2025 by EE

namespace SmartBot.Mulligan
{
    [Serializable]
    public class UniversalMulligan : MulliganProfile
    {
        // Initialize the log and list to store cards to keep
        private string log;

        // List to store cards to keep during the mulligan phase
        private List<Card.Cards> CardsToKeep = new List<Card.Cards>();

        // Local list of mulligan cards from HSReplay
        private List<HSReplayHelper.HSReplayMulligan> mulliganCards;

        // StringBuilder to display the cards to keep
        private StringBuilder keptCardBuilder = new StringBuilder("\nCards to Keep:");

        // StringBuilder to display the cards to choose from
        private StringBuilder choiceCardsBuilder = new StringBuilder("\nCards to choose from:");

        // Get current play mode
        private string _mode = Bot.CurrentMode().ToString();

        // Directories 
        private readonly string _mulliganProfilesDirectory = Directory.GetCurrentDirectory() + @"\MulliganProfiles\";

        // Bot mulligan handling
        public List<Card.Cards> HandleMulligan(List<Card.Cards> choices, Card.CClass opponentClass, Card.CClass ownClass)
        {
            string deckIdFile = Path.Combine(_mulliganProfilesDirectory, @"{MODE}\DeckId.json");
            string deckMulliganFile = Path.Combine(_mulliganProfilesDirectory, @"{MODE}\MulliganAllDecks.json");
            string mulliganFile = Path.Combine(_mulliganProfilesDirectory, @"{MODE}\mulligan.json");

            string deckId = null;
            string dataJson = null;
            string deckList = null;
            string matchDeck = null;

            log = string.Format("===== Universal Mulligan V5.2, Card definition V{0} ===EE", CurrentVersion());

            string mode = DeckMode(_mode);

            try
            {
                // Prepare file paths based on the current mode
                string deckIdPath = deckIdFile.Replace("{MODE}", mode);
                string deckMulliganPath = deckMulliganFile.Replace("{MODE}", mode);
                string mulliganPath = mulliganFile.Replace("{MODE}", mode);

                switch (mode)
                {
                    case "Standard":
                    case "Wild":
                        {
                            // Read deck ID file and mulligan data for all decks
                            deckList = File.ReadAllText(deckIdPath);

                            // Preload deck card IDs
                            var myDeck = Bot.CurrentDeck().Cards.Select(c => CardTemplate.LoadFromId(c).Id).ToList();

                            // Get the best matching deck ID from HSReplay
                            deckId = DeckId(deckList, myDeck, ownClass);

                            // Read mulligan data for the matched deck ID
                            dataJson = File.ReadAllText(deckMulliganPath);

                            // Get mulligan data for the matched deck ID
                            mulliganCards = HSReplayHelper.ExternalMulliganData(dataJson, deckId);

                            // Fallback to generic mulligan if no high-value cards found
                            if (!mulliganCards.Any() || mulliganCards.Count(m => choices.Contains(m.Key) && m.Value > 49 && CardTemplate.LoadFromId(m.Key).Cost < 4) == 0)
                            {
                                matchDeck = "Mulligan profiles from mulligan file, mode: " + mode;
                                dataJson = File.ReadAllText(mulliganPath);
                                mulliganCards = HSReplayHelper.InternalMulliganData(dataJson, ownClass);
                            }
                            else
                            {
                                matchDeck = deckId + " gameType = " + mode;
                            }
                            break;
                        }

                    case "Arena":
                    case "ArenaAuto":
                        {
                            // Use internal mulligan data for Arena
                            matchDeck = "Mulligan profiles from mulligan file, mode: " + mode;
                            dataJson = File.ReadAllText(mulliganPath);
                            mulliganCards = HSReplayHelper.InternalMulliganData(dataJson, ownClass);
                            break;
                        }

                    default:
                        {
                            // Use internal mulligan data for other modes
                            matchDeck = "Mulligan profiles from mulligan file, mode: " + mode;
                            dataJson = File.ReadAllText(mulliganFile.Replace("{MODE}", "Wild"));
                            mulliganCards = HSReplayHelper.InternalMulliganData(dataJson, ownClass);
                            break;
                        }
                }

                // Calculate overlap between mulligan data and current deck
                var deckCardIds = Bot.CurrentDeck().Cards.Select(c => CardTemplate.LoadFromId(c).Id).ToList();
                int overlap = mulliganCards.Count(m => deckCardIds.Contains(m.Key));

                AddLog("Selected Deck: " + Bot.CurrentDeck().Name);
                AddLog("Archetype: " + GetCurrentHSReplayArchetype());
                AddLog("Matched Deck: " + matchDeck);
                AddLog("Matching Entries: " + overlap + "/" + mulliganCards.Count);
                AddLog("\nFull Mulligan Data:");

                // Make card choices based on mulligan data
                CardChoice(choices, mulliganCards);
            }
            catch (Exception)
            {
                string fallbackPath = mulliganFile.Replace("{MODE}", mode.ToUpper());
                dataJson = File.ReadAllText(fallbackPath);

                mulliganCards = HSReplayHelper.InternalMulliganData(dataJson, ownClass);
                AddLog("Error from HSReplayMulligan: using default logic, " + mulliganCards.Count + " cards");

                CardChoice(choices, mulliganCards);
            }

            AddLog(choiceCardsBuilder + "\r\n" + keptCardBuilder);
            AddLog("===== End of mulligan ======");
            Bot.Log(log);

            return CardsToKeep;
        }

        // Get current deck mode from selected deck string
        private static string DeckMode(string mode)
        {
            if (mode != "Casual" && mode != "Practice")
                return mode;

            return Bot.CurrentDeck().Name.Substring(1, 1) == "S" ? "Standard" : "Wild";
        }

        // Method to handle the card choices based on the mulligan percentages
        private void CardChoice(List<Card.Cards> choices, List<HSReplayHelper.HSReplayMulligan> mulliganCards)
        {
            // Initialize the list of cards in the current deck, removing duplicates
            var myDeck = Bot.CurrentDeck().Cards.Distinct().Select(card => CardTemplate.LoadFromId(card).Id).ToList();

            double mulliganValue = 0;
            Card.Cards bestLowCost = Card.Cards.GAME_005;

            // Iterate through the mulligan cards and log the choices
            foreach (var mulliganCard in mulliganCards.OrderByDescending(card => card.Value))
            {
                if (myDeck.Contains(mulliganCard.Key) || choices.Contains(mulliganCard.Key))
                {
                    AddLog(string.Format("{0} - Kept : {1}%", CardTemplate.LoadFromId(mulliganCard.Key).Name, mulliganCard.Value.ToString("0.00")));
                }

                // If the card is in the current deck or choices, add it to the kept cards
                if (choices.Contains(mulliganCard.Key))
                {
                    if (mulliganCard.Value >= 75)
                    {
                        keptCardBuilder.AppendFormat("\n{0} - Kept : {1}%", CardTemplate.LoadFromId(mulliganCard.Key).Name, mulliganCard.Value.ToString("0.00"));
                        Keep(mulliganCard.Key);
                    }

                    // If the card is a low-cost card and has a higher keep percentage than the current best, update the best card to keep
                    if (mulliganCard.Value > mulliganValue && CardTemplate.LoadFromId(mulliganCard.Key).Cost < 4)
                    {
                        mulliganValue = mulliganCard.Value;
                        bestLowCost = mulliganCard.Key;
                    }

                    // Append the card to the choice cards string
                    choiceCardsBuilder.AppendFormat("\n{0} - Kept : {1}%", CardTemplate.LoadFromId(mulliganCard.Key).Name, mulliganCard.Value.ToString("0.00"));
                }
            }

            // If no cards were kept and the mulligan value is above 49%, keep the best low-cost card
            if (!CardsToKeep.Any() && mulliganValue > 49)
            {
                var card = CardTemplate.LoadFromId(bestLowCost);
                choiceCardsBuilder.AppendFormat("\n\nSelecting best, low cost card:\n{0} - Kept : {1}%", card.Name, mulliganValue.ToString("0.00"));
                keptCardBuilder.AppendFormat("\n{0} - Kept : {1}%", card.Name, mulliganValue.ToString("0.00"));
                Keep(bestLowCost);
            }
        }

        // Add cards to keep list
        private void Keep(Card.Cards id)
        {
            CardsToKeep.Add(id);
        }

        // Combine all Addlog, includes return and new line
        private void AddLog(string message)
        {
            log += "\r\n" + message;
        }

        // Get Current HSReplay Archetype
        private static string GetCurrentHSReplayArchetype()
        {
            var HSReplayArchetype = HSReplayArchetypesMatcher.DetectArchetype(Bot.GetSelectedDecks()[0].Cards, Bot.GetSelectedDecks()[0].Class, 30);
            if (HSReplayArchetype == null || HSReplayArchetype is HSReplayArchetypeError)
                return string.Empty; // Archetype couldn't be found
            return HSReplayArchetype.Name;
        }

        // New_Class
        // Extract and sort the required mulligan data received from HSReplay into new formatted list
        public class HSReplayHelper
        {
            private static List<HSReplayMulligan> cards = new List<HSReplayMulligan>();

            // Mulligan Standard and Wild
            public static List<HSReplayMulligan> ExternalMulliganData(string contents, string deckId)
            {
                cards.Clear();

                var root = JsonConvert.DeserializeObject<Dictionary<string, List<MulliganRoot>>>(contents);

                if (root == null || !root.ContainsKey(deckId))
                    return cards;

                // Get only the entries for the requested class/deck id
                var cardEntries = root[deckId];

                foreach (var card in cardEntries)
                {
                    var template = CardTemplate.TemplateList.FirstOrDefault(x => x.Value.Id.ToString() == card.card_id);

                    if (template.Value != null) // ensure match found
                    {
                        var info = new HSReplayMulligan
                        {
                            Key = template.Key,
                            Value = card.opening_hand_winrate
                        };
                        cards.Add(info);
                    }
                }

                return cards;
            }

            // Get best kept mulligan percentage from "mulligan Keep Table" from HSReplay for Arena, Twist, Practice and TavernBrawl
            public static List<HSReplayMulligan> InternalMulliganData(string jsonData, Card.CClass ownClass)
            {
                cards.Clear();

                CardRoot deckDataHSReplay = JsonConvert.DeserializeObject<CardRoot>(jsonData);

                var className = ownClass.ToString().ToUpper();
                var deckData = typeof(CardRoot).GetProperty(className)?.GetValue(deckDataHSReplay, null) as List<CardScore>;

                if (deckData == null) return cards; // safety check

                foreach (var card_by_dbf_Id in deckData)
                {
                    var info = new HSReplayMulligan
                    {
                        Key = CardTemplate.TemplateList.FirstOrDefault(x => x.Value.Id.ToString() == card_by_dbf_Id.card_id).Key,
                        Value = card_by_dbf_Id.mulligan_score
                    };
                    cards.Add(info);
                }

                return cards;
            }

            // Class representing the mulligan information for a specific card
            public class HSReplayMulligan
            {
                public Card.Cards Key; // The ID of the card
                public double Value; // Percentage of times the card is kept in the mulligan
            }
        }
        // End of HSReplayHelper class

        // Method to calculate the match points for a given decklist and deck
        public static int GetMatchPoint(string deckList, List<Card.Cards> deck)
        {
            const string pattern = @"\[(?<Id>\d+),(?<count>\d+)]";
            int count = 0;

            foreach (Match match in Regex.Matches(deckList, pattern))
            {
                int cardDbfId = int.Parse(match.Groups["Id"].Value);
                int cardCount = int.Parse(match.Groups["count"].Value);

                // Find the card ID in the CardTemplate list based on the DbfId
                var cardId = CardTemplate.TemplateList.FirstOrDefault(x => x.Value.DbfId == cardDbfId).Key;

                // Count how many times this card appears in the given deck
                int cardCountInDeck = deck.Count(c => c == cardId);

                // Award points if the card exists, and bonus if both have 2 copies
                count += (cardCountInDeck > 0 ? 1 : 0);
                count += (cardCountInDeck == 2 && cardCount == 2 ? 1 : 0);
            }
            return count;
        }

        // Method to get the deck ID for the closest match to the given deck in the data
        public static string DeckId(string jsonData, List<Card.Cards> deck, Card.CClass ownClass)
        {
            string bestId = null;
            int bestPoints = 0, bestGames = 0;

            var deckRoot = JsonConvert.DeserializeObject<DeckRoot>(jsonData);
            if (deckRoot == null) return null;

            var propertyInfo = typeof(DeckRoot).GetProperty(ownClass.ToString().Trim().ToUpper());
            if (propertyInfo == null) return null;
            var deck_IDs = propertyInfo.GetValue(deckRoot) as List<Deck>;
            if (deck_IDs == null) return null;

            foreach (var id in deck_IDs)
            {
                int points = GetMatchPoint(id.deck_list, deck);

                if (points > bestPoints || (points == bestPoints && id.total_games > bestGames))
                {
                    bestPoints = points;
                    bestGames = id.total_games;
                    bestId = id.deck_id;
                }
            }
            return bestId;
        }

        // Get current version from EE_Information.txt
        private string CurrentVersion()
        {
            string discoverCCDirectory = Path.Combine(Directory.GetCurrentDirectory(), "DiscoverCC");
            string infoPath = Path.Combine(discoverCCDirectory, "EE_Information.txt");

            // Create file if it doesn't exist
            if (!File.Exists(infoPath))
                return null;

            // Read first line and extract version number using regex
            string firstLine = File.ReadLines(infoPath).First();
            Match match = Regex.Match(firstLine, @"\((.+?)\)");
            if (match.Success)
                return match.Groups[1].Value;
            return null;
        }

        // *** Newtonsoft.Json ***
        // ============================================================
        // HSReplay Mulligan Score Models
        // Used for deserializing class‑specific mulligan score data
        // ============================================================

        public class CardScore
        {
            public string card_id { get; set; }
            public double mulligan_score { get; set; }
        }

        public class CardRoot
        {
            public List<CardScore> ALL { get; set; }
            public List<CardScore> DEATHKNIGHT { get; set; }
            public List<CardScore> DEMONHUNTER { get; set; }
            public List<CardScore> DRUID { get; set; }
            public List<CardScore> HUNTER { get; set; }
            public List<CardScore> MAGE { get; set; }
            public List<CardScore> PALADIN { get; set; }
            public List<CardScore> PRIEST { get; set; }
            public List<CardScore> ROGUE { get; set; }
            public List<CardScore> SHAMAN { get; set; }
            public List<CardScore> WARLOCK { get; set; }
            public List<CardScore> WARRIOR { get; set; }
        }

        // ============================================================
        // HSReplay Opening Hand Winrate Model
        // Used for deserializing single‑card opening hand winrate data
        // ============================================================

        public class MulliganRoot
        {
            public string card_id { get; set; }
            public double opening_hand_winrate { get; set; }
        }

        // ============================================================
        // HSReplay Mulligan Data Models
        // Used for deserializing mulligan.json and class‑specific data
        // ============================================================

        public class Deck
        {
            public string deck_id { get; set; }
            public string deck_list { get; set; }
            public int total_games { get; set; }
            public double win_rate { get; set; }
        }

        public class DeckRoot
        {
            public List<Deck> DEATHKNIGHT { get; set; }
            public List<Deck> DEMONHUNTER { get; set; }
            public List<Deck> DRUID { get; set; }
            public List<Deck> HUNTER { get; set; }
            public List<Deck> MAGE { get; set; }
            public List<Deck> PALADIN { get; set; }
            public List<Deck> PRIEST { get; set; }
            public List<Deck> ROGUE { get; set; }
            public List<Deck> SHAMAN { get; set; }
            public List<Deck> WARLOCK { get; set; }
            public List<Deck> WARRIOR { get; set; }
        }
    }
}
