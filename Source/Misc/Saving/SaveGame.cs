using System.Globalization;
using System.Numerics;
using System.Text;

namespace Atlas2D
{
    public class SaveGame
    {
        public string FilePath { get; }

        private readonly string[] delimiters = new string[]
        {
            ":f:",
            ":i:",
            ":s:",
            ":v:",
            ":t:",
            ":b:"
        };

        public SaveGame(string filePath)
        {
            FilePath = filePath;
            string directory = Path.GetDirectoryName(filePath);
            if (directory != null && directory != string.Empty && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (!File.Exists(filePath))
            {
                File.Create(filePath).Close();
            }
            Read();
        }

        public static void Delete(string filePath)
        {
            if(Verify(filePath))
            {
                File.Delete(filePath);
            }
        }

        public static bool Verify(string filePath)
        {
            return File.Exists(filePath) &&
                Base64Decode(File.ReadAllText(filePath)).StartsWith("Save:");
        }

        public bool Remove(string key)
        {
            Dictionary<string, string> keyValuePairs = Read();
            if(keyValuePairs.ContainsKey(key))
            {
                keyValuePairs.Remove(key);
                WriteFile(keyValuePairs);
                return true;
            }
            else
            {
                return false;
            }
        }

        private Dictionary<string, string> Read()
        {
            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>();
            try
            {
                string data = Base64Decode(File.ReadAllText(FilePath));
                if (data.StartsWith("Save:")) data = data.Substring(5);
                string[] parts = data.Split('>');
                if (parts.Length < 2)
                {
                    return keyValuePairs;
                }
                for (int i = 0; i < parts.Length; i += 2)
                {
                    string key = parts[i];
                    string value = parts[i + 1];
                    keyValuePairs.Add(key, value);
                }
                return keyValuePairs;
            }
            catch(Exception e)
            {
                Debug.LogError(e.Message);
                Debug.LogError($"Invalid save file, could not read. [{FilePath}]");
                return keyValuePairs;
            }
        }

        private void Write(string key, string value)
        {
            key = key.Replace('>', '-');
            value = value.Replace('>', '-');
            Dictionary<string, string> keyValuePairs = Read();
            if (keyValuePairs.ContainsKey(key))
            {
                keyValuePairs[key] = value;
            }
            else
            {
                keyValuePairs.Add(key, value);
            }
            WriteFile(keyValuePairs);
        }

        private void WriteFile(Dictionary<string, string> keyValuePairs)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("Save:");
            int i = 0;
            foreach (var pair in keyValuePairs)
            {
                builder.Append(pair.Key);
                builder.Append(">");
                builder.Append(pair.Value);
                i++;
                if (i < keyValuePairs.Count)
                {
                    builder.Append(">");
                }
            }
            // Written to a temp file first, so a crash mid-write can't leave a half-written save
            string tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, Base64Encode(builder.ToString()));
            File.Move(tempPath, FilePath, true);
        }

        public void SaveFloat(string key, float value)
        {
            Write(key, $"f:{FormatFloat(value)}");
        }

        public void SaveInt(string key, int value)
        {
            Write(key, $"i:{value.ToString(CultureInfo.InvariantCulture)}");
        }

        public void SaveString(string key, string value)
        {
            Write(key, $"s:{Escape(value)}");
        }

        public void SaveVector2(string key, Vector2 value)
        {
            Write(key, $"v:{FormatVector(value)}");
        }

        public void SaveBool(string key, bool value)
        {
            Write(key, $"t:{value}");
        }

        public void SaveBytes(string key, byte[] value)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("b:");
            for (int i = 0; i < value.Length; i++)
            {
                builder.Append(value[i].ToString());
                if(i + 1 < value.Length)
                {
                    builder.Append(',');
                }
            }
            Write(key, builder.ToString());
        }

        public void Save(string key, ISaveable value)
        {
            StringBuilder builder = new StringBuilder();
            SaveData data = value.GetSaveData();
            builder.Append("S:");
            builder.Append(":f:");
            if (data.Floats.Count > 0)
            {
                builder.AppendJoin(',', data.Floats.Select(FormatFloat));
            }
            builder.Append(":i:");
            if (data.Ints.Count > 0)
            {
                builder.AppendJoin(',', data.Ints.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            }
            builder.Append(":s:");
            if (data.Strings.Count > 0)
            {
                builder.AppendJoin(',', data.Strings.Select(Escape));
            }
            builder.Append(":v:");
            if (data.Vectors.Count > 0)
            {
                builder.AppendJoin(',', data.Vectors.Select(FormatVector));
            }
            builder.Append(":t:");
            if (data.Bools.Count > 0)
            {
                builder.AppendJoin(',', data.Bools.Select(b => b.ToString()));
            }
            builder.Append(":b:");
            if (data.Bytes.Count > 0)
            {
                builder.AppendJoin(',', data.Bytes.Select(b => b.ToString(CultureInfo.InvariantCulture)));
            }
            Write(key, builder.ToString());
        }

        // Numbers are always saved with '.' decimals, whatever the PC's language
        private static string FormatFloat(float value) => value.ToString(CultureInfo.InvariantCulture);

        private static float ParseFloat(string text)
        {
            text = text.Trim();
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return value;
            // Saves from before this fix used the PC's language, like "1,5" in German
            return float.Parse(text, NumberStyles.Float, CultureInfo.CurrentCulture);
        }

        private static string FormatVector(Vector2 value) => $"({FormatFloat(value.X)},{FormatFloat(value.Y)})";

        // Strings can't contain the characters the file format uses to split values, so those are escaped
        private static string Escape(string value)
        {
            if (value.Length == 0) return "%00";
            return value.Replace("%", "%25").Replace(",", "%2C").Replace(">", "%3E").Replace(":", "%3A");
        }

        private static string Unescape(string value)
        {
            if (value == "%00") return "";
            return value.Replace("%3A", ":").Replace("%3E", ">").Replace("%2C", ",").Replace("%25", "%");
        }

        public float? GetFloat(string key)
        {
            Dictionary<string, string> keyValuePairs = Read();
            if(keyValuePairs.ContainsKey(key))
            {
                string[] parts = keyValuePairs[key].Split(':');
                if (parts[0] == "f")
                {
                    try
                    {
                        return ParseFloat(parts[1]);
                    }
                    catch
                    {
                        Debug.LogError($"Invalid float with key of [{key}] in save file [{FilePath}]");
                        throw new Exception();
                    }
                }
                else
                {
                    Debug.LogError($"Saved value with key [{key}] in save file [{FilePath}] is not a float.");
                    throw new Exception();
                }
            }
            else
            {
                return null;
            }
        }

        public int? GetInt(string key)
        {
            Dictionary<string, string> keyValuePairs = Read();
            if (keyValuePairs.ContainsKey(key))
            {
                string[] parts = keyValuePairs[key].Split(':');
                if (parts[0] == "i")
                {
                    try
                    {
                        return int.Parse(parts[1], CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        Debug.LogError($"Invalid int with key of [{key}] in save file [{FilePath}]");
                        throw new Exception();
                    }
                }
                else
                {
                    Debug.LogError($"Saved value with key [{key}] in save file [{FilePath}] is not a int.");
                    throw new Exception();
                }
            }
            else
            {
                return null;
            }
        }

        public string? GetString(string key)
        {
            Dictionary<string, string> keyValuePairs = Read();
            if (keyValuePairs.ContainsKey(key))
            {
                string[] parts = keyValuePairs[key].Split(':', 2);
                if (parts[0] == "s")
                {
                    try
                    {
                        return Unescape(parts[1]);
                    }
                    catch
                    {
                        Debug.LogError($"Invalid string with key of [{key}] in save file [{FilePath}]");
                        throw new Exception();
                    }
                }
                else
                {
                    Debug.LogError($"Saved value with key [{key}] in save file [{FilePath}] is not a string.");
                    throw new Exception();
                }
            }
            else
            {
                return null;
            }
        }

        public Vector2? GetVector2(string key)
        {
            Dictionary<string, string> keyValuePairs = Read();
            if (keyValuePairs.ContainsKey(key))
            {
                string[] parts = keyValuePairs[key].Split(':');
                if (parts[0] == "v")
                {
                    try
                    {
                        string[] values = parts[1].Split(",", 2);
                        return new Vector2(ParseFloat(values[0].Trim('(')),
                                            ParseFloat(values[1].Trim(')')));
                    }
                    catch
                    {
                        Debug.LogError($"Invalid vector2 with key of [{key}] in save file [{FilePath}]");
                        throw new Exception();
                    }
                }
                else
                {
                    Debug.LogError($"Saved value with key [{key}] in save file [{FilePath}] is not a vector2.");
                    throw new Exception();
                }
            }
            else
            {
                return null;
            }
        }

        public bool? GetBool(string key)
        {
            Dictionary<string, string> keyValuePairs = Read();
            if (keyValuePairs.ContainsKey(key))
            {
                string[] parts = keyValuePairs[key].Split(':');
                if (parts[0] == "t")
                {
                    try
                    {
                        return bool.Parse(parts[1]);
                    }
                    catch
                    {
                        Debug.LogError($"Invalid bool with key of [{key}] in save file [{FilePath}]");
                        throw new Exception();
                    }
                }
                else
                {
                    Debug.LogError($"Saved value with key [{key}] in save file [{FilePath}] is not a bool.");
                    throw new Exception();
                }
            }
            else
            {
                return null;
            }
        }

        public byte[] GetBytes(string key)
        {
            Dictionary<string, string> keyValuePairs = Read();
            if (keyValuePairs.ContainsKey(key))
            {
                string[] parts = keyValuePairs[key].Split(':');
                if (parts[0] == "b")
                {
                    try
                    {
                        string[] byteStrings = parts[1].Split(",");
                        byte[] bytes = new byte[byteStrings.Length];
                        for(int i = 0; i < byteStrings.Length; i++)
                        {
                            bytes[i] = byte.Parse(byteStrings[i], CultureInfo.InvariantCulture);
                        }
                        return bytes;
                    }
                    catch
                    {
                        Debug.LogError($"Invalid byte array with key of [{key}] in save file [{FilePath}]");
                        throw new Exception();
                    }
                }
                else
                {
                    Debug.LogError($"Saved value with key [{key}] in save file [{FilePath}] is not a byte array.");
                    throw new Exception();
                }
            }
            else
            {
                return null;
            }
        }

        public SaveData? GetSave(string key)
        {
            Dictionary<string, string> keyValuePairs = Read();
            if (keyValuePairs.ContainsKey(key))
            {
                string[] parts = keyValuePairs[key].Split(':', 2);
                if (parts[0] == "S")
                {
                    SaveData data = new SaveData();
                    string[] arrayStrings = parts[1].Split(delimiters, StringSplitOptions.None);
                    for(int i = 0; i < delimiters.Length; i++)
                    {
                        try
                        {
                            if (arrayStrings[i + 1] != string.Empty)
                            {
                                string[] elementStrings = arrayStrings[i + 1].Split(',');
                                for (int x = 0; x < elementStrings.Length; x++)
                                {
                                    if (elementStrings[x] == string.Empty) continue;
                                    switch (i)
                                    {
                                        case 0:
                                            data.Floats.Add(ParseFloat(elementStrings[x]));
                                            break;
                                        case 1:
                                            data.Ints.Add(int.Parse(elementStrings[x], CultureInfo.InvariantCulture));
                                            break;
                                        case 2:
                                            data.Strings.Add(Unescape(elementStrings[x]));
                                            break;
                                        case 3:
                                            // Also reads the old "<x, y>" format, which was saved as "<x, y-"
                                            data.Vectors.Add(new Vector2(ParseFloat(elementStrings[x].TrimStart('(', '<', ' ')),
                                                ParseFloat(elementStrings[x + 1].TrimEnd(')', '>', '-', ' '))));
                                            x++;
                                            break;
                                        case 4:
                                            data.Bools.Add(bool.Parse(elementStrings[x]));
                                            break;
                                        case 5:
                                            data.Bytes.Add(byte.Parse(elementStrings[x], CultureInfo.InvariantCulture));
                                            break;
                                    }
                                }
                            }
                        }
                        catch(Exception e)
                        {
                            Debug.LogError(e.Message);
                            Debug.LogError($"Could not parse array type of [{delimiters[i]}] in save data with key [{key}] " +
                                $"in save file [{FilePath}]");
                            throw new Exception();
                        }
                        
                    }

                    return data;
                }
                else
                {
                    Debug.LogError($"Saved value with key [{key}] in save file [{FilePath}] is not a save data object.");
                    throw new Exception();
                }
            }
            else
            {
                return null;
            }
        }

        private static string Base64Encode(string plainText)
        {
            //var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            //return Convert.ToBase64String(plainTextBytes);
            return plainText;
        }

        private static string Base64Decode(string base64EncodedData)
        {
            //var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
            //return Encoding.UTF8.GetString(base64EncodedBytes);
            return base64EncodedData;
        }
    }
}
