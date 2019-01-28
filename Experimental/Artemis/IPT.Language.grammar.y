%namespace GameRes.Formats.Artemis
%partial
%parsertype IPTParser
%visibility internal
%tokentype Token

%union { 
	public int n; 
	public string s; 
	public IPTObject o;
}

%start input

%token NUMBER STRING_LITERAL IDENTIFIER

%%

input: root_definition ;

root_definition: IDENTIFIER '=' object { RootObject[$1.s] = $3.o; }
	       ;

object: '{' { BeginObject(); }
        decl_list optional_comma
        '}' { EndObject(); }
      ;

decl_list: statement
	 | decl_list ',' statement
	 ;

optional_comma: ',' | /* empty */ ;

statement: definition | lvalue ;

definition: IDENTIFIER '=' value { CurrentObject[$1.s] = $3.Value; }
          ;

lvalue: value { CurrentObject.Values.Add ($1.Value); }
      ;

value: object | string | number ;

string: STRING_LITERAL ;

number: NUMBER ;

%%