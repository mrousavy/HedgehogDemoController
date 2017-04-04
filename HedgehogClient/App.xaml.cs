﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace HedgehogClient {
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {

        public App() : base() {
            Exit += delegate {
                //Dispose Xbox Thread
                if(ControlKeys.XboxControllerThread != null)
                    ControlKeys.XboxControllerThread.Abort();
            };
        }
    }
}
