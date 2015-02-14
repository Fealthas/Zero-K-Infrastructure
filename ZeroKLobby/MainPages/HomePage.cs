﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZeroKLobby.MainPages
{
    public partial class HomePage : UserControl
    {
        public HomePage()
        {
            InitializeComponent();
        }

        private void singleplayerButton_Click(object sender, EventArgs e)
        {
            Program.MainWindow.SwitchPage(MainWindow.MainPages.SinglePlayer);
        }

        private void multiplayerButton_Click(object sender, EventArgs e)
        {
            Program.MainWindow.SwitchPage(MainWindow.MainPages.MultiPlayer);
        }

        private void exitButton_Click(object sender, EventArgs e)
        {
            Program.ShutDown();
        }
    }
}
