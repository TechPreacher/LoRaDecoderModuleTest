//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace SenderModule
{
    // Network server configuration
    public class IotEdgeConfiguration
    {
        // Gets/sets the interval data is sent
        public int Interval { get; set; } = 2000;

        // Gets/sets the decoder
        public string Decoder { get; set; }

        // Creates a new instance of NetworkServerConfiguration
        public IotEdgeConfiguration()
        {    
        }

        // Creates a new instance of NetworkServerConfiguration by reading values from environment variables
        public static IotEdgeConfiguration CreateFromEnviromentVariables()
        {
            var config = new IotEdgeConfiguration();

            // Create case insensitive dictionary from environment variables
            var envVars = new CaseInsensitiveEnvironmentVariables(Environment.GetEnvironmentVariables());           

            config.Interval = envVars.GetEnvVar("INTERVAL", config.Interval);
            config.Decoder = envVars.GetEnvVar("DECODER", string.Empty);

            return config;
        }
    }
}