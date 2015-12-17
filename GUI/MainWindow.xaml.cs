﻿using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Engine;
using Botclient;
using Label = System.Windows.Controls.Label;
using players = Engine.players;

namespace connect4
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private List<IPlayer> _players = new List<IPlayer>();
        private Label[][] labels;
        private Game game;
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            game = new Game((byte)Width.Value, (byte)Height.Value);
            _players.Add(new GUI_player(this, players.Alice));
            if (AI_checkbox.IsChecked.Value)
            {
                _players.Add(new Bot(players.Bob, (byte)difficulty_slider.Value));
            }
            else
            {
                _players.Add(new GUI_player(this, players.Bob));
            }
            settings_grid.Visibility = Visibility.Collapsed;
            game_grid.Visibility = Visibility.Visible;
            for (int i = 0; i < Width.Value; i++)
            {
                var coldef = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
                game_grid.ColumnDefinitions.Add(coldef);
            }
            for (int i = 0; i < Height.Value; i++)
            {
                var rowdef = new RowDefinition { Height = new GridLength(1, GridUnitType.Star) };
                game_grid.RowDefinitions.Add(rowdef);
            }
            labels = new Label[(uint)Width.Value][];
            for (int x = 0; x < Width.Value; x++)
            {
                labels[x] = new Label[(uint)Height.Value];
                for (int y = 0; y < Height.Value; y++)
                {

                    Label label = new Label();
                    game_grid.Children.Add(label);

                    label.MouseEnter += label_enter;
                    label.MouseRightButtonDown += label_click;
                    label.MouseLeftButtonDown += label_click;
                    label.SetValue(Grid.ColumnProperty, x);
                    label.SetValue(Grid.RowProperty, y);
                    labels[x][y] = label;
                    label.Tag = new byte[] { (byte)x, (byte)y };
                }
            }
        }

        internal bool get_button_pressed(players player)
        {
            throw new NotImplementedException();
        }

        internal byte get_numeric(players player)
        {
            throw new NotImplementedException();
        }

        private void label_click(object sender, MouseEventArgs e)
        {
            byte y = ((byte[])((Label)sender).Tag)[1];
            string s = string.Empty;
            while (!game.add_stone(y, game.next_player, ref s))
            {
                Console.WriteLine(s);
            }

        }
        private void label_enter(object sender, MouseEventArgs e)
        {
            paint_black();
            byte[] coords = (byte[])((Label)sender).Tag;
            update_field(coords[0], coords[1]);
        }

        private void update_field(byte? x = null, byte? y = null)
        {
            if (x != null)
            {
                for (int _y = 0; _y < labels[0].Length; _y++)
                {
                    labels[(byte)x][_y].Background = new SolidColorBrush(Colors.Blue);
                }
            }
            if (y != null)
            {
                for (int _x = 0; _x < labels.Length; _x++)
                {
                    labels[_x][(byte)y].Background = new SolidColorBrush(Colors.Blue);
                }
            }
        }

        private void paint_black()
        {
            foreach (Label[] _labels in labels)
            {
                foreach (Label label in _labels)
                {
                    label.Background = new SolidColorBrush(Colors.Black);
                }
            }
        }
    }
}
