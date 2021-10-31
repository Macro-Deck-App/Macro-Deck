using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.Utils
{
    public class StringSearch
    {

        public static bool StringContains(string str, string search)
        {
            return str.ToLower().Replace(" ", "").Contains(search.ToLower().Replace(" ", ""));


        }

    }
}
