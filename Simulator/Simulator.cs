﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Schema;
using Engine;
using Botclient;
using Networker;

namespace Simulator
{
    class Simulator
    {

        private readonly byte width = 7;
        private readonly byte height = 6;
        private readonly uint max_games = 1;
        private byte games_won_alice;
        private byte games_won_bob;
        static private log_modes log_mode = log_modes.verbose;
        /// <summary>
        /// Tries to execute the given turn from the given player.
        /// If the given turn is not possible for whatever reason, the player is asked again.
        /// Returns false if it fails to drop a stone;
        /// </summary>
        /// <param name="player"></param>
        private bool do_turn(IPlayer player, Game game)
        {
            if (game.stones_count == game.width * game.height) //If the whole field is full of stones and no one has won, it's a tie
            {
                if (log_mode >= log_modes.verbose)
                    Console.WriteLine($"The field is full, it is a tie if no one has won");
                return false;
            }
            string s = "";
            int counter = 0;
            while (!game.add_stone(player.get_turn(game.get_field()), player.player, ref s)) //Try to add a stone fails. If that fails, log the error and try it again.
            {
                counter++;
                if (log_mode >= log_modes.debug)
                    Console.WriteLine($"{s} ({counter} tries)");
                if (counter < 100) continue;
                if (log_mode >= log_modes.only_errors)
                    Console.WriteLine("Exceeded maximum of tries for a turn");
                return false;
            }
            return true;
        }

        private players do_game(Game game, ref List<byte> history)
        {
            game = new Game(width, height);
            if (log_mode >= log_modes.debug)
                Console.WriteLine($"Created new game of {game.get_field().Width} by {game.get_field().Height}");
            var _players = new List<IPlayer>() //A fancy list to prevent the use of if-statements
                {
                    new Bot(players.Alice),
                    new Bot(players.Bob)
                };
            while (true)
            {
                bool tie = !do_turn(_players.Find(player => player.player == game.next_player), game); //Execute the turn the player who's turn it is. If do_turn returns false, it is a tie;
                //First add the indication of the winner of the match to the history-list
                //Then add the history itself
                if (game.has_won(players.Alice))
                {
                    history.Add((byte)network_codes.game_history_alice);
                    history.AddRange(game.history);
                    return players.Alice;
                }
                if (game.has_won(players.Bob))
                {
                    history.Add((byte)network_codes.game_history_alice);
                    history.AddRange(game.history);
                    return players.Bob;
                }
                if (tie)
                {
                    return players.Empty;
                }
            }
        }

        private void send_history(List<List<byte>> histories)
        {
            List<byte> data = new List<byte>();
            data.Add((byte)network_codes.game_history_array);
            //Concatenate all the game-histories into one byte-array;
            foreach (var history in histories)
            {
                data.AddRange(history);
            }
            data.Add((byte)network_codes.end_of_stream);
            Stopwatch sw = new Stopwatch();
            Requester.request(data.ToArray());

        }
        /// <summary>
        /// Loop through the given amount of games, and log some stuff in the meantime
        /// </summary>
        private void loop_games()
        {
            //A stopwatch to measure how much time we spend on simulating these games
            var sw = new Stopwatch();
            sw.Start();
            //Jagged array to store the history of all the games
            //TODO switch list to array
            List<List<byte> > histories = new List<List<byte>>();
            for (int game_count = 0; game_count < max_games; game_count++)
            {
                var game = new Game(width, height);
                List<byte> history = new List<byte>();
                players victourious_player = do_game(game, ref history);

                int turns = history.Count - 1; //The amount of turns this game lasted. 1 is subtracted for the winner indication at the start.
                if (victourious_player == players.Alice)
                {
                    games_won_alice++;

                    histories.Add(history);

                    if (log_mode >= log_modes.verbose)
                        Console.WriteLine($"Alice won her {games_won_alice}th game after {turns} turns");
                }
                else if (victourious_player == players.Bob)
                {
                    games_won_bob++;

                    histories.Add(history);

                    if (log_mode >= log_modes.verbose)
                        Console.WriteLine($"Bob won his {games_won_bob}th game after {turns} turns");
                }
                else
                {
                    if (log_mode >= log_modes.verbose)
                        Console.WriteLine($"The game was a tie");
                }
            }
            sw.Stop();
            send_history(histories);
            TimeSpan elapsed = sw.Elapsed;
            if (log_mode > 0)
            {
                Console.WriteLine($"Simulation of {max_games} game(s) finished in {elapsed}");
                Console.WriteLine($"Alice won {games_won_alice} games, Bob won {games_won_bob} and {max_games - games_won_alice - games_won_bob} were a tie;");
            }
        }
        /// <summary>
        /// Initialize 1 option from a list of command-line arguments and return the value.
        /// </summary>
        /// <param name="args">List of all arguments and options</param>
        /// <param name="cmd_char">The character which indicates the option</param>
        /// <param name="arg_name">A readable name of the option which is printed on the screen</param>
        /// <param name="min">The minimum value of the argument</param>
        /// <param name="max">The maximum value of the argument</param>
        /// <param name="default_value">The default value of the argument. This value will be returned if the argument was invalid</param>
        /// <returns></returns>
        private static uint parse_arg(List<string> args, string cmd_char, string arg_name, byte min, uint max, byte default_value)
        {
            int index = args.IndexOf("-" + cmd_char);
            if (index != -1)
            {
                try
                {
                    uint option = uint.Parse(args[index + 1]); //As the output of this relies on user input, this can give errors.
                    if (option >= min && option <= max)
                    {
                        if (log_mode >= log_modes.essential)
                            Console.WriteLine($"{arg_name} is set to {option}");
                        return option;
                    }

                    if (log_mode >= log_modes.only_errors)
                        Console.WriteLine($"{arg_name} was given outside of the boundaries {min} and {max}");
                    if (log_mode >= log_modes.essential)
                        Console.WriteLine($"{arg_name} defaulted to {default_value}");
                }
                catch (FormatException) //Formatexception for the uint.parse
                {
                    if (log_mode >= log_modes.only_errors)
                        Console.WriteLine($"{arg_name} was given in the wrong format");
                }
            }
            //Return the default value when we had an exception or the found parameter was outside of the given boundaries
            return default_value;
        }
        /// <summary>
        /// Initializes the simulator
        /// </summary>
        /// <param name="_args">The command-line arguments</param>
        public Simulator(string[] _args)
        {
            List<string> args = new List<string>(_args);
            log_mode = (log_modes)parse_arg(args, "m", "log _modes", 0, 5, 2);
            width = (byte)parse_arg(args, "w", "width", 2, 20, 7);
            height = (byte)parse_arg(args, "h", "height", 2, 20, 6);
            max_games = parse_arg(args, "g", "maximum of games", 1, uint.MaxValue, 1);
            Console.ReadLine();
        }
        /// <param name="args">
        /// -l  [0-5]   log _modes (default 2) <see cref="log_modes"/>
        /// -w  [>0]    width  of the playing field (default 7)
        /// -h  [>0]    height of the playing field (default 6)
        /// -g  [>0]    amount of games to simulate (default 1)
        /// </param>
        private static void Main(string[] args)
        {
            Simulator sim = new Simulator(args);
            sim.loop_games();
            Console.ReadLine();
        }
    }
}
