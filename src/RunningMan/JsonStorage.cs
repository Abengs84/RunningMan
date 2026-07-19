using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RunningMan.Storage
{
    /// <summary>
    /// JSON file persistence for track layout and completed race records.
    /// </summary>
    public static class JsonStorage
    {
        private static string _rootPath;
        private static TrackConfig _track = new TrackConfig();
        private static RaceDatabase _database = new RaceDatabase();
        private static AllowedGearRules _allowedGear = AllowedGearRules.CreateDefaults();
        private static List<Vector3Data> _wrBoards = new List<Vector3Data>();
        private static List<Vector3Data> _rulesBoards = new List<Vector3Data>();

        public static TrackConfig Track => _track;
        public static RaceDatabase Database => _database;
        public static AllowedGearRules AllowedGear => _allowedGear;
        public static List<Vector3Data> WrBoards => _wrBoards;
        public static List<Vector3Data> RulesBoards => _rulesBoards;

        public static string TrackFilePath => Path.Combine(_rootPath, "track.json");
        public static string DatabaseFilePath => Path.Combine(_rootPath, "races.json");
        public static string AllowedGearFilePath => Path.Combine(_rootPath, "allowed_gear.json");
        public static string WrBoardsFilePath => Path.Combine(_rootPath, "wr_boards.json");
        public static string RulesBoardsFilePath => Path.Combine(_rootPath, "rules_boards.json");
        public static string ExportFilePath => Path.Combine(_rootPath, "export.json");

        public static void Initialize(string bepInExConfigPath)
        {
            var relative = ModConfig.SavePath.Value?.Trim() ?? "RunningMan/";
            _rootPath = Path.Combine(bepInExConfigPath, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(_rootPath);
            Reload();
        }

        public static void Reload()
        {
            _track = LoadTrack() ?? new TrackConfig();
            _database = LoadDatabase() ?? new RaceDatabase();
            _allowedGear = LoadAllowedGear() ?? AllowedGearRules.CreateDefaults();
            _wrBoards = LoadWrBoards() ?? new List<Vector3Data>();
            _rulesBoards = LoadRulesBoards() ?? new List<Vector3Data>();
            RunningManPlugin.Log.LogInfo($"RunningMan data loaded from {_rootPath}");
        }

        public static void SaveWrBoards()
        {
            WriteJson(WrBoardsFilePath, new WrBoardsFile { Boards = _wrBoards });
        }

        public static void SaveRulesBoards()
        {
            WriteJson(RulesBoardsFilePath, new WrBoardsFile { Boards = _rulesBoards });
        }

        public static void SetWrBoards(List<Vector3Data> boards, bool save = true)
        {
            _wrBoards = CloneBoardList(boards);
            if (save)
            {
                SaveWrBoards();
            }
        }

        public static void SetRulesBoards(List<Vector3Data> boards, bool save = true)
        {
            _rulesBoards = CloneBoardList(boards);
            if (save)
            {
                SaveRulesBoards();
            }
        }

        private static List<Vector3Data> CloneBoardList(List<Vector3Data> boards)
        {
            return boards != null
                ? boards.Where(b => b != null).Select(b => new Vector3Data(b.X, b.Y, b.Z)).ToList()
                : new List<Vector3Data>();
        }

        public static void SaveTrack()
        {
            WriteJson(TrackFilePath, _track);
        }

        public static void SaveDatabase()
        {
            WriteJson(DatabaseFilePath, _database);
        }

        public static void SaveAllowedGear()
        {
            WriteJson(AllowedGearFilePath, _allowedGear);
        }

        public static void SetAllowedGear(AllowedGearRules rules, bool save = true)
        {
            if (rules == null)
            {
                return;
            }

            _allowedGear = rules.Clone();
            if (save)
            {
                SaveAllowedGear();
            }
        }

        public static void ResetAllowedGearToDefaults()
        {
            _allowedGear = AllowedGearRules.CreateDefaults();
            SaveAllowedGear();
        }

        public static void ExportAll()
        {
            var export = new ExportBundle
            {
                ExportedAt = DateTime.UtcNow.ToString("o"),
                Track = _track,
                Races = _database.Runs,
                PersonalBests = _database.PersonalBests,
                AllowedGear = _allowedGear
            };
            WriteJson(ExportFilePath, export);
        }

        private static TrackConfig LoadTrack()
        {
            return ReadJsonFile<TrackConfig>(TrackFilePath);
        }

        private static RaceDatabase LoadDatabase()
        {
            return ReadJsonFile<RaceDatabase>(DatabaseFilePath);
        }

        private static AllowedGearRules LoadAllowedGear()
        {
            return ReadJsonFile<AllowedGearRules>(AllowedGearFilePath);
        }

        private static List<Vector3Data> LoadWrBoards()
        {
            var file = ReadJsonFile<WrBoardsFile>(WrBoardsFilePath);
            return file?.Boards ?? new List<Vector3Data>();
        }

        private static List<Vector3Data> LoadRulesBoards()
        {
            var file = ReadJsonFile<WrBoardsFile>(RulesBoardsFilePath);
            return file?.Boards ?? new List<Vector3Data>();
        }

        private static T ReadJsonFile<T>(string path) where T : class
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                return TypedJson.Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                RunningManPlugin.Log.LogError($"Failed to read {path}: {ex.Message}");
                return null;
            }
        }

        private static void WriteJson(string path, object data)
        {
            try
            {
                var json = TypedJson.Serialize(data);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                RunningManPlugin.Log.LogError($"Failed to write {path}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// All persisted race results and personal bests.
    /// </summary>
    [Serializable]
    public sealed class RaceDatabase
    {
        public List<RaceRecord> Runs = new List<RaceRecord>();
        public Dictionary<string, RaceRecord> PersonalBests = new Dictionary<string, RaceRecord>(StringComparer.Ordinal);
        public Dictionary<string, RaceRecord> LastRuns = new Dictionary<string, RaceRecord>(StringComparer.Ordinal);
    }

    [Serializable]
    public sealed class ExportBundle
    {
        public string ExportedAt;
        public TrackConfig Track;
        public List<RaceRecord> Races;
        public Dictionary<string, RaceRecord> PersonalBests;
        public AllowedGearRules AllowedGear;
    }

    [Serializable]
    public sealed class WrBoardsFile
    {
        public List<Vector3Data> Boards = new List<Vector3Data>();
    }

    /// <summary>
    /// Strongly-typed JSON reader/writer for RunningMan data files.
    /// Uses manual parsing to avoid external dependencies.
    /// </summary>
    internal static class TypedJson
    {
        public static string Serialize(object value)
        {
            return JsonWriter.Write(value);
        }

        public static T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var root = JsonParser.Parse(json);
            return ConvertTo<T>(root);
        }

        private static T ConvertTo<T>(object value) where T : class
        {
            if (value == null)
            {
                return null;
            }

            if (typeof(T) == typeof(TrackConfig))
            {
                return (T)(object)ParseTrackConfig(value as Dictionary<string, object>);
            }

            if (typeof(T) == typeof(RaceDatabase))
            {
                return (T)(object)ParseRaceDatabase(value as Dictionary<string, object>);
            }

            if (typeof(T) == typeof(AllowedGearRules))
            {
                return (T)(object)ParseAllowedGear(value as Dictionary<string, object>);
            }

            if (typeof(T) == typeof(WrBoardsFile))
            {
                return (T)(object)ParseWrBoardsFile(value as Dictionary<string, object>);
            }

            return null;
        }

        private static WrBoardsFile ParseWrBoardsFile(Dictionary<string, object> dict)
        {
            var file = new WrBoardsFile();
            if (dict == null)
            {
                return file;
            }

            foreach (var item in GetList(dict, "Boards"))
            {
                var vector = ParseVector(item as Dictionary<string, object>);
                if (vector != null)
                {
                    file.Boards.Add(vector);
                }
            }

            return file;
        }

        private static AllowedGearRules ParseAllowedGear(Dictionary<string, object> dict)
        {
            var rules = AllowedGearRules.CreateDefaults();
            if (dict == null)
            {
                return rules;
            }

            rules.Helmet = GetString(dict, "Helmet") ?? rules.Helmet;
            rules.Chest = GetString(dict, "Chest") ?? rules.Chest;
            rules.Legs = GetString(dict, "Legs") ?? rules.Legs;
            rules.Cape = GetString(dict, "Cape") ?? rules.Cape;
            rules.AllowedHandItems = GetString(dict, "AllowedHandItems") ?? rules.AllowedHandItems;
            rules.AntiStingPrefab = GetString(dict, "AntiStingPrefab") ?? rules.AntiStingPrefab;
            rules.RatatoskPrefab = GetString(dict, "RatatoskPrefab") ?? rules.RatatoskPrefab;
            if (dict.ContainsKey("RequiredAntiSting"))
            {
                rules.RequiredAntiSting = GetInt(dict, "RequiredAntiSting");
            }

            if (dict.ContainsKey("RequiredRatatosk"))
            {
                rules.RequiredRatatosk = GetInt(dict, "RequiredRatatosk");
            }

            rules.SaladPrefab = GetString(dict, "SaladPrefab") ?? rules.SaladPrefab;
            rules.BloodPuddingPrefab = GetString(dict, "BloodPuddingPrefab") ?? rules.BloodPuddingPrefab;
            rules.MushroomOmelettePrefab = GetString(dict, "MushroomOmelettePrefab") ?? rules.MushroomOmelettePrefab;
            if (dict.ContainsKey("RequiredSalad"))
            {
                rules.RequiredSalad = GetInt(dict, "RequiredSalad");
            }

            if (dict.ContainsKey("RequiredBloodPudding"))
            {
                rules.RequiredBloodPudding = GetInt(dict, "RequiredBloodPudding");
            }

            if (dict.ContainsKey("RequiredMushroomOmelette"))
            {
                rules.RequiredMushroomOmelette = GetInt(dict, "RequiredMushroomOmelette");
            }

            return rules;
        }

        private static TrackConfig ParseTrackConfig(Dictionary<string, object> dict)
        {
            if (dict == null)
            {
                return new TrackConfig();
            }

            return new TrackConfig
            {
                Name = GetString(dict, "Name") ?? string.Empty,
                StartGate = ParseGate(GetDict(dict, "StartGate")),
                FinishGate = ParseGate(GetDict(dict, "FinishGate")),
                Checkpoints = ParseCheckpoints(GetList(dict, "Checkpoints"))
            };
        }

        private static RaceDatabase ParseRaceDatabase(Dictionary<string, object> dict)
        {
            var database = new RaceDatabase();
            if (dict == null)
            {
                return database;
            }

            foreach (var item in GetList(dict, "Runs"))
            {
                var record = ParseRaceRecord(item as Dictionary<string, object>);
                if (record != null)
                {
                    database.Runs.Add(record);
                }
            }

            foreach (var pair in GetDict(dict, "PersonalBests"))
            {
                database.PersonalBests[pair.Key] = ParseRaceRecord(pair.Value as Dictionary<string, object>);
            }

            foreach (var pair in GetDict(dict, "LastRuns"))
            {
                database.LastRuns[pair.Key] = ParseRaceRecord(pair.Value as Dictionary<string, object>);
            }

            return database;
        }

        private static RaceRecord ParseRaceRecord(Dictionary<string, object> dict)
        {
            if (dict == null)
            {
                return null;
            }

            return new RaceRecord
            {
                Player = GetString(dict, "Player"),
                PlayerId = GetString(dict, "PlayerId"),
                Date = GetString(dict, "Date"),
                FinishedAt = GetString(dict, "FinishedAt"),
                TotalTimeMs = GetLong(dict, "TotalTimeMs"),
                TotalTime = GetString(dict, "TotalTime"),
                CheckpointTimesMs = GetLongList(dict, "CheckpointTimesMs"),
                CheckpointTimes = GetStringList(dict, "CheckpointTimes"),
                SplitTimesMs = GetLongList(dict, "SplitTimesMs"),
                SplitTimes = GetStringList(dict, "SplitTimes"),
                CheckpointCount = GetInt(dict, "CheckpointCount"),
                TrackId = GetString(dict, "TrackId") ?? string.Empty
            };
        }

        private static List<Checkpoint> ParseCheckpoints(List<object> list)
        {
            var checkpoints = new List<Checkpoint>();
            if (list == null)
            {
                return checkpoints;
            }

            foreach (var item in list)
            {
                var dict = item as Dictionary<string, object>;
                if (dict == null)
                {
                    continue;
                }

                checkpoints.Add(new Checkpoint(
                    GetInt(dict, "Index"),
                    ParseGate(GetDict(dict, "Gate"))));
            }

            return checkpoints;
        }

        private static RaceGate ParseGate(Dictionary<string, object> dict)
        {
            if (dict == null)
            {
                return new RaceGate();
            }

            return new RaceGate
            {
                PointA = ParseVector(GetDict(dict, "PointA")),
                PointB = ParseVector(GetDict(dict, "PointB")),
                Forward = ParseVector(GetDict(dict, "Forward"))
            };
        }

        private static Vector3Data ParseVector(Dictionary<string, object> dict)
        {
            if (dict == null)
            {
                return new Vector3Data();
            }

            return new Vector3Data(
                (float)GetDouble(dict, "X"),
                (float)GetDouble(dict, "Y"),
                (float)GetDouble(dict, "Z"));
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out var value)
                ? value as Dictionary<string, object>
                : null;
        }

        private static List<object> GetList(Dictionary<string, object> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out var value)
                ? value as List<object>
                : null;
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out var value) ? value as string : null;
        }

        private static int GetInt(Dictionary<string, object> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out var value) ? Convert.ToInt32(value) : 0;
        }

        private static long GetLong(Dictionary<string, object> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out var value) ? Convert.ToInt64(value) : 0L;
        }

        private static double GetDouble(Dictionary<string, object> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out var value) ? Convert.ToDouble(value) : 0d;
        }

        private static List<long> GetLongList(Dictionary<string, object> dict, string key)
        {
            var list = GetList(dict, key);
            if (list == null)
            {
                return new List<long>();
            }

            return list.Select(Convert.ToInt64).ToList();
        }

        private static List<string> GetStringList(Dictionary<string, object> dict, string key)
        {
            var list = GetList(dict, key);
            if (list == null)
            {
                return new List<string>();
            }

            return list.Select(item => item as string).Where(item => item != null).ToList();
        }
    }

    /// <summary>
    /// Pretty-printed JSON writer with indentation for human-readable files.
    /// </summary>
    internal static class JsonWriter
    {
        public static string Write(object value)
        {
            var builder = new StringBuilder();
            WriteValue(builder, value, 0);
            builder.AppendLine();
            return builder.ToString();
        }

        private static void WriteValue(StringBuilder builder, object value, int indent)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    break;
                case string text:
                    builder.Append('"').Append(Escape(text)).Append('"');
                    break;
                case bool boolean:
                    builder.Append(boolean ? "true" : "false");
                    break;
                case int number:
                    builder.Append(number);
                    break;
                case long number:
                    builder.Append(number);
                    break;
                case float number:
                    builder.Append(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case double number:
                    builder.Append(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case IEnumerable<RaceRecord> records:
                    WriteArray(builder, records.Cast<object>(), indent);
                    break;
                case IEnumerable<Checkpoint> checkpoints:
                    WriteArray(builder, checkpoints.Cast<object>(), indent);
                    break;
                case IEnumerable<string> strings:
                    WriteArray(builder, strings.Cast<object>(), indent);
                    break;
                case IEnumerable<long> longs:
                    WriteArray(builder, longs.Cast<object>(), indent);
                    break;
                case IEnumerable<Vector3Data> vectors:
                    WriteArray(builder, vectors.Cast<object>(), indent);
                    break;
                case IDictionary<string, RaceRecord> recordMap:
                    WriteObject(builder, recordMap.ToDictionary(pair => pair.Key, pair => (object)pair.Value), indent);
                    break;
                case IDictionary<string, object> map:
                    WriteObject(builder, map, indent);
                    break;
                default:
                    WriteObject(builder, FieldsToDictionary(value), indent);
                    break;
            }
        }

        private static void WriteArray(StringBuilder builder, IEnumerable<object> items, int indent)
        {
            builder.AppendLine("[");
            var array = items.ToList();
            for (var i = 0; i < array.Count; i++)
            {
                builder.Append(' ', (indent + 1) * 2);
                WriteValue(builder, array[i], indent + 1);
                builder.Append(i < array.Count - 1 ? "," : string.Empty);
                builder.AppendLine();
            }

            builder.Append(' ', indent * 2);
            builder.Append(']');
        }

        private static void WriteObject(StringBuilder builder, IDictionary<string, object> map, int indent)
        {
            builder.AppendLine("{");
            var keys = map.Keys.ToList();
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                builder.Append(' ', (indent + 1) * 2);
                builder.Append('"').Append(Escape(key)).Append('"').Append(": ");
                WriteValue(builder, map[key], indent + 1);
                builder.Append(i < keys.Count - 1 ? "," : string.Empty);
                builder.AppendLine();
            }

            builder.Append(' ', indent * 2);
            builder.Append('}');
        }

        private static Dictionary<string, object> FieldsToDictionary(object value)
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var field in value.GetType().GetFields())
            {
                dict[field.Name] = field.GetValue(value);
            }

            return dict;
        }

        private static string Escape(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }

    /// <summary>
    /// Lightweight JSON parser returning dictionaries and lists.
    /// </summary>
    internal static class JsonParser
    {
        public static object Parse(string json)
        {
            return new Reader(json).ParseValue();
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _index;

            public Reader(string text)
            {
                _text = text;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                switch (Peek())
                {
                    case '{':
                        return ParseObject();
                    case '[':
                        return ParseArray();
                    case '"':
                        return ParseString();
                    case 't':
                    case 'f':
                        return ParseBool();
                    default:
                        return ParseNumber();
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                Expect('{');
                SkipWhitespace();
                if (Peek() == '}')
                {
                    Read();
                    return dict;
                }

                while (true)
                {
                    SkipWhitespace();
                    var key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    dict[key] = ParseValue();
                    SkipWhitespace();
                    if (Peek() == '}')
                    {
                        Read();
                        break;
                    }

                    Expect(',');
                }

                return dict;
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                Expect('[');
                SkipWhitespace();
                if (Peek() == ']')
                {
                    Read();
                    return list;
                }

                while (true)
                {
                    list.Add(ParseValue());
                    SkipWhitespace();
                    if (Peek() == ']')
                    {
                        Read();
                        break;
                    }

                    Expect(',');
                }

                return list;
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (_index < _text.Length)
                {
                    var ch = Read();
                    if (ch == '"')
                    {
                        break;
                    }

                    if (ch == '\\')
                    {
                        var escaped = Read();
                        builder.Append(escaped switch
                        {
                            '"' => '"',
                            '\\' => '\\',
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            _ => escaped
                        });
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                }

                return builder.ToString();
            }

            private bool ParseBool()
            {
                if (Match("true"))
                {
                    return true;
                }

                Match("false");
                return false;
            }

            private object ParseNumber()
            {
                var start = _index;
                while (_index < _text.Length && "0123456789+-.".Contains(Peek()))
                {
                    Read();
                }

                var token = _text.Substring(start, _index - start);
                if (token.Contains("."))
                {
                    return double.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
                }

                if (long.TryParse(token, out var longValue))
                {
                    return longValue;
                }

                return int.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
            }

            private void SkipWhitespace()
            {
                while (_index < _text.Length && char.IsWhiteSpace(Peek()))
                {
                    _index++;
                }
            }

            private char Peek() => _text[_index];
            private char Read() => _text[_index++];
            private void Expect(char ch)
            {
                SkipWhitespace();
                if (Read() != ch)
                {
                    throw new FormatException($"Expected '{ch}' at {_index}.");
                }
            }

            private bool Match(string token)
            {
                SkipWhitespace();
                if (_index + token.Length > _text.Length)
                {
                    return false;
                }

                if (_text.Substring(_index, token.Length) != token)
                {
                    return false;
                }

                _index += token.Length;
                return true;
            }
        }
    }
}
