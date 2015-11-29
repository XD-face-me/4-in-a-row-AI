﻿using System;
using System.Net;
using System.Net.Sockets;
using Engine;
using System.Text;
using System.Threading.Tasks;
using Networker;

namespace Botclient
{
    public class Bot : IPlayer
    {
        public players player { get; }
        public Bot(players player)
        {
            this.player = player;
        }

        public byte get_turn(Field field)
        {
            var column = Requester.request(field.getStorage())[0];
            Console.WriteLine($"Tried to drop a stone in colmun {column}");
            return column;
        }

    }
}
