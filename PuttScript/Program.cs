using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace PuttScript
{
    class Program
    {
        //Table Format:
        //<HEX>=<UTF8> (something like: "2C=A" or "015D=[Item]")
        //Special Cases:
        //- Commands could include %% in the HEX and UTF8
        //Like this: 05%%=[C$%%] or FE%%%%=[S$%%%%]

        //Regex: ^([0-9a-fA-F%]{2,})=(.+)$

        const string regexrule = "^([0-9a-fA-F%]{2,})=(.+)$";

        static void Main(string[] args)
        {
            Console.WriteLine("PuttScript\nby LuigiBlood\n");
            if (args.Length == 3)
            {
                if (args[0] != "-d" && args[0] != "-e")
                    Console.WriteLine("Unknown option");

                if (!File.Exists(args[1]) || !File.Exists(args[2]))
                    Console.WriteLine("One of the files does not exist");

                //Do the thing
                if (args[0] == "-d")
                    Decode(args[1], args[2]);
                else if (args[0] == "-e")
                    Encode(args[1], args[2]);
            }
            else if (args.Length == 0)
            {
                Console.WriteLine("Usage:\nputtscript <options> <table file> <input file>\nOptions:\n  -d: Decode Binary into Text file\n  -e: Encode Text file into Binary");
            }
            else
            {
                Console.WriteLine("Not enough arguments");
            }
        }

        static int GetDict(string tablefile, int order, out List<Tuple<string, string>> dict)
        {
            //Key = Hex, Value = UTF8

            dict = new List<Tuple<string, string>>();
            StreamReader table = File.OpenText(tablefile);
            string test = "";
            int line = 0;

            while (!table.EndOfStream)
            {
                test = table.ReadLine();
                if (!Regex.IsMatch(test, regexrule))
                {
                    Console.WriteLine("ERROR:\nCan't recognize at table line " + line + ": \"" + test + "\"");
                    return 1;
                }
                //string[] value = Regex.Split(test, regexrule);
                string[] value = test.Split('=', 2);
                if ((value[0].Length & 1) == 1)
                {
                    Console.WriteLine("ERROR:\nTable line " + line + ": \"" + test + "\"");
                    return 1;
                }
                string hex = value[0].ToUpperInvariant();
                string text = value[1].Replace('\\', '\n');

                if (order != 0)
                    text = text.Trim('\n');

                dict.Add(new Tuple<string, string>(hex, text));
                line++;
            }

            table.Close();

            dict.Sort(delegate(Tuple<string, string> x, Tuple<string, string> y)
            {
                string x_str = (order == 0) ? x.Item1 : x.Item2;
                string y_str = (order == 0) ? y.Item1 : y.Item2;

                if (x_str.Length != y_str.Length)
                {
                    return y_str.Length - x_str.Length;
                }
                else
                {
                    if (order != 0)
                    {
                        if (x_str.Contains("%"))
                            return 1;
                        else if (y_str.Contains("%"))
                            return -1;
                    }
                    return -x_str.CompareTo(y_str);
                }
            });

            foreach (Tuple<string, string> x in dict)
                Console.WriteLine(x.Item1 + ":" + x.Item2);
            return 0;
        }

        static int Decode(string tablefile, string inputfile)
        {
            //Input File = Binary

            List<Tuple<string, string>> dict;
            int DictError = GetDict(tablefile, 0, out dict);
            if (DictError != 0)
                return DictError;

            //Convert to Text
            FileStream input_f = File.OpenRead(inputfile);
            string text_out = "";
            
            int length = (int)Math.Min(input_f.Length, 0x1000000L);
            byte[] input = new byte[length];

            input_f.Read(input, 0, length);
            input_f.Close();

            Console.WriteLine("");
            for (int i = 0; i < input.Length;)
            {
                Console.SetCursorPosition(0, Console.CursorTop - 1);
                Console.WriteLine("Progress: " + Math.Ceiling((double)i / input.Length * 100d) + "%  ");
                int index_use = -1;

                //Assume the list contains the longest first, therefore it should find the match without necessarily going through the entire list
                for (int j = 0; j < dict.Count; j++)
                {
                    int score = 0;
                    
                    if (!dict[j].Item1.Contains("%%") && (i + (dict[j].Item1.Length / 2) <= input.Length))
                    {
                        string byte_str = "";
                        for (int k = 0; k < (dict[j].Item1.Length / 2); k++)
                        {
                            byte_str += input[i + k].ToString("X2");
                        }

                        if (dict[j].Item1 == byte_str)
                            score = 1;
                    }
                    else if (i + (dict[j].Item1.Length / 2) <= input.Length)
                    {
                        for (int k = 0; k < (dict[j].Item1.Length / 2); k++)
                        {
                            string byte_tbl = dict[j].Item1.Substring(k * 2, 2);
                            string byte_in = input[i + k].ToString("X2");

                            if (byte_tbl == "%%")
                            {
                                score++;
                            }
                            else if (byte_tbl == byte_in)
                            {
                                score += 2;
                            }
                            else
                            {
                                score = 0;
                                break;
                            }
                        }
                    }

                    if (score != 0)
                    {
                        index_use = j;
                        break;
                    }
                }

                if (index_use == -1)
                {
                    Console.WriteLine("ERROR:\nInput file offset 0x" + i.ToString("X") + ":" + input[i].ToString("X2") + "... not defined in table");
                    return 1;
                }

                //Deal with %%
                string table_in = dict[index_use].Item1;
                string table_out = dict[index_use].Item2;

                if (table_in.Contains("%%"))
                {
                    for (int k = 0; k < (table_in.Length / 2); k++)
                    {
                        string byte_tbl = table_in.Substring(k * 2, 2);
                        string byte_in = input[i + k].ToString("X2");

                        if (byte_tbl == "%%")
                        {
                            int index = table_out.IndexOf("%%");
                            table_out = table_out.Remove(index, 2).Insert(index, byte_in);
                        }
                    }
                }

                text_out += table_out;
                i += dict[index_use].Item1.Length / 2;
            }
            StreamWriter text_f = new StreamWriter(File.Open(Path.GetFileNameWithoutExtension(inputfile) + "_out.txt", FileMode.Create));
            text_f.Write(text_out);
            text_f.Close();

            Console.WriteLine("Written as " + Path.GetFileNameWithoutExtension(inputfile) + "_out.txt");

            return 0;
        }

        static int Encode(string tablefile, string inputfile)
        {
            //Input File = Text

            List<Tuple<string, string>> dict;
            int DictError = GetDict(tablefile, 1, out dict);
            if (DictError != 0)
                return DictError;
            //Key = UTF8, Value = Hex

            //Convert to Bin
            string[] text_in = File.ReadAllLines(inputfile);
            List<byte> bin_out = new List<byte>();

            Console.WriteLine("");

            for (int i = 0; i < text_in.Length; i++)
            {
                Console.SetCursorPosition(0, Console.CursorTop - 1);
                Console.WriteLine("Progress: " + Math.Ceiling((double)i / text_in.Length * 100d) + "%  ");
                //text_in[i] = text_in[i].Trim('\n');
                for (int c = 0; c < text_in[i].Length;)
                {
                    int index_use = -1;

                    for (int j = 0; j < dict.Count; j++)
                    {
                        int score = 0;

                        if ((c + dict[j].Item2.Length) > text_in[i].Length)
                            continue;

                        if (!dict[j].Item1.Contains("%%") && !dict[j].Item2.Contains("%%") && (dict[j].Item2.Equals(text_in[i].Substring(c, dict[j].Item2.Length))))
                        {
                            score = 1;
                        }
                        else
                        {
                            for (int k = 0; k < dict[j].Item2.Length; k++)
                            {
                                string char_tbl = dict[j].Item2.Substring(k, 1);
                                string chat_in = text_in[i].Substring(c + k, 1);

                                if (char_tbl == "%" && dict[j].Item1.Contains("%%"))
                                {
                                    score++;
                                }
                                else if (char_tbl == chat_in)
                                {
                                    score += 2;
                                }
                                else
                                {
                                    score = 0;
                                    break;
                                }
                            }
                        }

                        if (score != 0)
                        {
                            index_use = j;
                            break;
                        }
                    }

                    if (index_use == -1)
                    {
                        Console.WriteLine("ERROR:\nInput file line " + (i + 1).ToString() + ":\n\"" + text_in[i] + "\" not defined in table");
                        Console.WriteLine("^".PadLeft(c + 2));
                        return 1;
                    }

                    //Deal with %%
                    string table_in = dict[index_use].Item2;
                    string table_out = dict[index_use].Item1;
                    string text_input = text_in[i].Substring(c, table_in.Length);

                    //Console.WriteLine(table_in.Length + " : " + table_in + " - " + text_input.Length + " : " + text_input);

                    while (table_out.Contains("%%"))
                    {
                        int index_in = table_in.IndexOf("%%");
                        int index_out = table_out.IndexOf("%%");
                        table_in = table_in.Remove(index_in, 2).Insert(index_in, text_input.Substring(index_in, 2));
                        table_out = table_out.Remove(index_out, 2).Insert(index_out, text_input.Substring(index_in, 2));
                    }
                    /*
                    if (!table_in.Equals(text_input))
                    {
                        Console.WriteLine("ERROR:\nInput file line " + (i + 1).ToString() + ":\n\"" + text_in[i] + "\" problem");
                        Console.WriteLine("^".PadLeft(c + 1));
                        return 1;
                    }*/

                    for (int b = 0; b < table_out.Length / 2; b++)
                        bin_out.Add(Byte.Parse(table_out.Substring(b * 2, 2), NumberStyles.HexNumber));
                    c += text_input.Length;
                }
            }

            FileStream bin_f = File.Open(Path.GetFileNameWithoutExtension(inputfile) + "_out.bin", FileMode.Create);
            bin_f.Write(bin_out.ToArray(), 0, bin_out.Count);
            bin_f.Close();

            Console.WriteLine("Written as " + Path.GetFileNameWithoutExtension(inputfile) + "_out.bin");

            return 0;
        }
    }
}
