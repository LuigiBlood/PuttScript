using System;
using System.Collections.Generic;
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

        static int Decode(string tablefile, string inputfile)
        {
            //Input File = Binary

            List<Tuple<string, string>> dict = new List<Tuple<string, string>>();
            //Key = Hex, Value = UTF8

            //Set up Dictionary
            {
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
                    string[] value = Regex.Split(test, regexrule);
                    if ((value[0].Length & 1) == 1)
                    {
                        Console.WriteLine("ERROR:\nTable line " + line + ": \"" + test + "\"");
                        return 1;
                    }
                    Console.WriteLine(value.Length + " : " + value[1].ToUpperInvariant() + " : " + value[2]);
                    dict.Add(new Tuple<string, string>(value[1].ToUpperInvariant(), value[2].Replace('\\', '\n')));
                    line++;
                }

                table.Close();
            }

            //Convert to Text
            FileStream input_f = File.OpenRead(inputfile);
            string text_out = "";
            
            int length = (int)Math.Min(input_f.Length, 0x1000000L);
            byte[] input = new byte[length];

            input_f.Read(input, 0, length);
            input_f.Close();

            for (int i = 0; i < input.Length;)
            {
                List<int> indexes = new List<int>();
                //Search all keys that starts with this byte
                for (int j = 0; j < dict.Count; j++)
                {
                    if (dict[j].Item1.StartsWith(input[i].ToString("X2")))
                    {
                        indexes.Add(j);
                    }
                }

                //Console.WriteLine(input[i].ToString("X2") + ":" + indexes.Count);

                if (indexes.Count == 0)
                {
                    Console.WriteLine("ERROR:\nInput file offset 0x" + i.ToString("X") + ":" + input[i].ToString("X2") + " not defined in table");
                    return 1;
                }

                //See which one to use if there's more than one
                int index_use = indexes[0];
                if (indexes.Count > 1)
                {
                    List<int> scores = new List<int>();
                    for (int j = 0; j < indexes.Count; j++)
                    {
                        int score = 0;
                        for (int k = 0; k < (dict[indexes[j]].Item1.Length / 2); k++)
                        {
                            string byte_tbl = dict[indexes[j]].Item1.Substring(k * 2, 2);
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
                        scores.Add(score);
                    }

                    int total_score = 0;
                    int total_index = -1;
                    for (int j = 0; j < scores.Count; j++)
                    {
                        if (total_score < scores[j])
                        {
                            //Console.WriteLine(total_score + " < " +scores[j]);
                            total_score = scores[j];
                            total_index = j;
                        }
                    }

                    if (total_index == -1)
                    {
                        Console.WriteLine("ERROR:\nInput file offset 0x" + i.ToString("X") + ":" + input[i].ToString("X2") + " " + input[i + 1].ToString("X2") + " " + input[i + 2].ToString("X2") + " " + input[i + 3].ToString("X2") + "... not defined in table");
                        return 1;
                    }

                    index_use = indexes[total_index];
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

            StreamWriter text_f = new StreamWriter(File.OpenWrite(Path.GetFileNameWithoutExtension(inputfile) + "_out.txt"));
            text_f.Write(text_out);
            text_f.Close();

            Console.WriteLine("Written");

            return 0;
        }

        static int Encode(string tablefile, string inputfile)
        {
            //Input File = Text

            Dictionary<string, string> dict = new Dictionary<string, string>();
            //Key = UTF8, Value = Hex
            return 0;
        }
    }
}
