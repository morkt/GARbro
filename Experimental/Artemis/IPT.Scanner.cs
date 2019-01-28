using System;
using System.Collections.Generic;
using System.Text;

namespace GameRes.Formats.Artemis
{
    internal partial class IPTScanner
    {
        void GetNumber()
        {
            yylval.n = int.Parse (yytext);
            yylval.s = null;
        }

        void GetStringLiteral ()
        {
            yylval.s = yytext.Substring (1, yytext.Length-2);
        }

		public override void yyerror (string format, params object[] args)
		{
			base.yyerror (format, args);
            if (args.Length > 0)
                throw new YYParseException (string.Format (format, args));
            else
                throw new YYParseException (format);
		}
    }

    public class YYParseException : Exception
    {
        public YYParseException (string message) : base (message)
        {
        }
    }
}
