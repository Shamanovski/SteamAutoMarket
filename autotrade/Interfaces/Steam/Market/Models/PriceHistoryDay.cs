﻿using System;
using System.Collections.Generic;

namespace Market.Models
{
    public class PriceHistoryDay
    {
        public DateTime Date { get; set; }
        public List<PriceHistoryItem> History { get; set; }
    }
}