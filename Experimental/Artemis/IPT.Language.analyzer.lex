%namespace GameRes.Formats.Artemis
%scannertype IPTScanner
%visibility internal
%tokentype Token

%option stack, minimize, parser, verbose, persistbuffer, noembedbuffers 

Space		[ \t\v\n\f]
Number		[0-9]+

%{

%}

%%

{Space}+		/* skip */

{Number}		{ GetNumber(); return (int)Token.NUMBER; }

\"(\\.|[^\\"\n])*\"	{ GetStringLiteral(); return (int)Token.STRING_LITERAL; }

[a-zA-Z]+		{ yylval.s = yytext; return (int)Token.IDENTIFIER; }

"{"			{ return '{'; }

"}"			{ return '}'; }

"="			{ return '='; }

","			{ return ','; }

%%