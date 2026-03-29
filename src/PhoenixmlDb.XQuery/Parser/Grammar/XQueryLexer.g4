lexer grammar XQueryLexer;

// ==================== Keywords ====================

// FLWOR keywords
KW_FOR          : 'for';
KW_LET          : 'let';
KW_WHERE        : 'where';
KW_ORDER        : 'order';
KW_BY           : 'by';
KW_RETURN       : 'return';
KW_IN           : 'in';
KW_ALLOWING     : 'allowing';
KW_EMPTY        : 'empty';
KW_AT           : 'at';
KW_STABLE       : 'stable';
KW_ASCENDING    : 'ascending';
KW_DESCENDING   : 'descending';
KW_GREATEST     : 'greatest';
KW_LEAST        : 'least';
KW_COLLATION    : 'collation';
KW_GROUP        : 'group';
KW_COUNT        : 'count';
KW_WHILE        : 'while';
KW_MEMBER       : 'member';   // XPath 4.0: for member
KW_RECORD       : 'record';   // XPath 4.0: record type
KW_ENUM         : 'enum';     // XPath 4.0: enum type

// Window keywords
KW_TUMBLING     : 'tumbling';
KW_SLIDING      : 'sliding';
KW_WINDOW       : 'window';
KW_START        : 'start';
KW_END          : 'end';
KW_WHEN         : 'when';
KW_ONLY         : 'only';
KW_PREVIOUS     : 'previous';
KW_NEXT         : 'next';
KW_CURRENT      : 'current';

// Conditional keywords
KW_IF           : 'if';
KW_THEN         : 'then';
KW_ELSE         : 'else';

// Quantified
KW_SOME         : 'some';
KW_EVERY        : 'every';
KW_SATISFIES    : 'satisfies';

// Switch / typeswitch
KW_SWITCH       : 'switch';
KW_CASE         : 'case';
KW_DEFAULT      : 'default';
KW_TYPESWITCH   : 'typeswitch';

// Try/catch
KW_TRY          : 'try';
KW_CATCH        : 'catch';

// Logical / comparison
KW_AND          : 'and';
KW_OR           : 'or';
KW_NOT          : 'not';
KW_EQ           : 'eq';
KW_NE           : 'ne';
KW_LT           : 'lt';
KW_LE           : 'le';
KW_GT           : 'gt';
KW_GE           : 'ge';
KW_IS           : 'is';

// Arithmetic
KW_DIV          : 'div';
KW_IDIV         : 'idiv';
KW_MOD          : 'mod';

// Type keywords
KW_INSTANCE     : 'instance';
KW_OF           : 'of';
KW_TREAT        : 'treat';
KW_AS           : 'as';
KW_CASTABLE     : 'castable';
KW_CAST         : 'cast';
KW_TO           : 'to';
KW_UNION        : 'union';
KW_INTERSECT    : 'intersect';
KW_EXCEPT       : 'except';
KW_OTHERWISE    : 'otherwise';

// XQuery Update Facility keywords
KW_INSERT       : 'insert';
KW_DELETE       : 'delete';
KW_REPLACE      : 'replace';
KW_RENAME       : 'rename';
KW_COPY         : 'copy';
KW_MODIFY       : 'modify';
KW_INTO         : 'into';
KW_WITH         : 'with';
KW_NODES        : 'nodes';
KW_VALUE        : 'value';
KW_BEFORE       : 'before';
KW_AFTER        : 'after';
KW_FIRST        : 'first';
KW_LAST         : 'last';

// XQuery Full-Text keywords
KW_CONTAINS     : 'contains';
KW_FTAND        : 'ftand';
KW_FTOR         : 'ftor';
KW_FTNOT        : 'ftnot';
KW_PHRASE       : 'phrase';
KW_ANY          : 'any';
KW_ALL          : 'all';
KW_WORD         : 'word';
KW_WORDS        : 'words';
// KW_ORDERED already defined in prolog keywords
KW_DISTANCE     : 'distance';
KW_SAME         : 'same';
KW_SENTENCE     : 'sentence';
KW_PARAGRAPH    : 'paragraph';
KW_ENTIRE       : 'entire';
KW_CONTENT      : 'content';
KW_STEMMING     : 'stemming';
KW_LANGUAGE     : 'language';
KW_WILDCARDS    : 'wildcards';
KW_SENSITIVE    : 'sensitive';
KW_INSENSITIVE  : 'insensitive';
KW_DIACRITICS   : 'diacritics';
KW_STOP         : 'stop';
KW_THESAURUS    : 'thesaurus';
KW_USING        : 'using';
KW_NO           : 'no';

// Sequence type keywords
KW_ITEM         : 'item';
KW_NODE         : 'node';
KW_ELEMENT      : 'element';
KW_ATTRIBUTE    : 'attribute';
KW_TEXT         : 'text';
KW_COMMENT      : 'comment';
KW_DOCUMENT_NODE: 'document-node';
KW_PROCESSING_INSTRUCTION : 'processing-instruction';
KW_SCHEMA_ELEMENT : 'schema-element';
KW_SCHEMA_ATTRIBUTE : 'schema-attribute';
KW_NAMESPACE_NODE : 'namespace-node';
KW_EMPTY_SEQUENCE : 'empty-sequence';
KW_FUNCTION     : 'function';

// Axis keywords
KW_CHILD        : 'child';
KW_DESCENDANT   : 'descendant';
KW_SELF         : 'self';
KW_DESCENDANT_OR_SELF : 'descendant-or-self';
KW_FOLLOWING_SIBLING  : 'following-sibling';
KW_FOLLOWING    : 'following';
KW_PARENT       : 'parent';
KW_ANCESTOR     : 'ancestor';
KW_PRECEDING_SIBLING  : 'preceding-sibling';
KW_PRECEDING    : 'preceding';
KW_ANCESTOR_OR_SELF   : 'ancestor-or-self';
KW_NAMESPACE    : 'namespace';

// Constructor keywords
KW_DOCUMENT     : 'document';

// Map/array
KW_MAP          : 'map';
KW_ARRAY        : 'array';

// Prolog keywords
KW_XQUERY      : 'xquery';
KW_VERSION     : 'version';
KW_ENCODING    : 'encoding';
KW_MODULE      : 'module';
KW_DECLARE     : 'declare';
KW_VARIABLE    : 'variable';
KW_IMPORT      : 'import';
KW_SCHEMA      : 'schema';
KW_EXTERNAL    : 'external';
KW_OPTION      : 'option';
KW_CONSTRUCTION : 'construction';
KW_ORDERING    : 'ordering';
KW_ORDERED     : 'ordered';
KW_UNORDERED   : 'unordered';
KW_PRESERVE    : 'preserve';
KW_STRIP       : 'strip';
KW_BOUNDARY_SPACE : 'boundary-space';
KW_COPY_NAMESPACES : 'copy-namespaces';
KW_NO_PRESERVE : 'no-preserve';
KW_INHERIT     : 'inherit';
KW_NO_INHERIT  : 'no-inherit';
KW_BASE_URI    : 'base-uri';
KW_DEFAULT_COLLATION : 'default';

// ==================== Operators / Punctuation ====================

LPAREN          : '(';
RPAREN          : ')';
LBRACKET        : '[';
RBRACKET        : ']';
LBRACE          : '{';
RBRACE          : '}';
COMMA           : ',';
SEMICOLON       : ';';
COLON           : ':';
COLONCOLON      : '::';
DOT             : '.';
DOTDOT          : '..';
LESS_THAN_SLASH : '</';
SLASH_GREATER_THAN : '/>';
SLASH           : '/';
DSLASH          : '//';
PIPE            : '|';
PIPEPIPE        : '||';
DOLLAR          : '$';
ASSIGN          : ':=';
EQUALS          : '=';
NOT_EQUALS      : '!=';
LESS_THAN       : '<';
LESS_EQ         : '<=';
GREATER_THAN    : '>';
GREATER_EQ      : '>=';
LSHIFT          : '<<';
RSHIFT          : '>>';
PLUS            : '+';
MINUS           : '-';
STAR            : '*';
BANG            : '!';
QUESTION        : '?';
HASH            : '#';
PERCENT         : '%';
FAT_ARROW       : '=>';
THIN_ARROW      : '->';
AT_SIGN         : '@';

// ==================== Literals ====================

IntegerLiteral
    : Digits
    ;

DecimalLiteral
    : '.' Digits
    | Digits '.' [0-9]*
    ;

DoubleLiteral
    : ('.' Digits | Digits ('.' [0-9]*)?) [eE] [+-]? Digits
    ;

StringLiteral
    : '"' (~["] | '""' | PredefinedEntityRef | CharRef)* '"'
    | '\'' (~['] | '\'\'' | PredefinedEntityRef | CharRef)* '\''
    ;

// ==================== Names ====================

// URIQualifiedName: Q{namespace-uri}local-name (EQName syntax)
URIQualifiedName
    : 'Q' '{' ~[{}]* '}' NameStartChar NameChar*
    ;

// BracedURILiteral: Q{namespace-uri} without local-name (for wildcards like Q{uri}*)
BracedURILiteral
    : 'Q' '{' ~[{}]* '}'
    ;

// NCName: a name without a colon (XML Name Character minus ':')
NCName
    : NameStartChar NameChar*
    ;

// ==================== Whitespace / Comments ====================

WS
    : [ \t\r\n]+ -> skip
    ;

// XQuery comments: (: ... :) with nesting
XQueryComment
    : '(:' (XQueryComment | .)*? ':)' -> skip
    ;

// ==================== String Constructor tokens ====================
// XQuery 4.0 string constructors: ``[content `{expr}` more]``

STRING_CONSTRUCTOR_OPEN  : '``[' -> pushMode(STRING_CONSTRUCTOR);

// ==================== START_TAG mode ====================
// Inside an element start tag: <element attr="val" ...>

mode START_TAG;

START_TAG_WS        : [ \t\r\n]+ -> skip;
START_TAG_CLOSE     : '>' -> mode(ELEM_CONTENT);
START_TAG_EMPTY_CLOSE : '/>' -> popMode;
START_TAG_EQUALS    : '=';
// Simple attribute values (no enclosed expressions)
START_TAG_STRING
    : '"' (~[<"{] | PredefinedEntityRef | CharRef)* '"'
    | '\'' (~[<'{] | PredefinedEntityRef | CharRef)* '\''
    ;
// Attribute values containing enclosed expressions: attr="{expr}" or attr="text {expr} text"
ATTR_VALUE_DQ_OPEN  : '"' -> pushMode(ATTR_VALUE_DQ);
ATTR_VALUE_SQ_OPEN  : '\'' -> pushMode(ATTR_VALUE_SQ);
START_TAG_QNAME     : NameStartChar NameChar* (':' NameStartChar NameChar*)?;

// ==================== ATTR_VALUE_DQ mode ====================
// Inside a double-quoted attribute value with potential enclosed expressions

mode ATTR_VALUE_DQ;

ATTR_DQ_LBRACE        : '{' -> pushMode(DEFAULT_MODE);
ATTR_DQ_ESCAPE_LBRACE : '{{';
ATTR_DQ_ESCAPE_RBRACE : '}}';
ATTR_DQ_CHAR          : (~["{<&] | PredefinedEntityRef | CharRef)+;
ATTR_DQ_CLOSE         : '"' -> popMode;

// ==================== ATTR_VALUE_SQ mode ====================
// Inside a single-quoted attribute value with potential enclosed expressions

mode ATTR_VALUE_SQ;

ATTR_SQ_LBRACE        : '{' -> pushMode(DEFAULT_MODE);
ATTR_SQ_ESCAPE_LBRACE : '{{';
ATTR_SQ_ESCAPE_RBRACE : '}}';
ATTR_SQ_CHAR          : (~['{<&] | PredefinedEntityRef | CharRef)+;
ATTR_SQ_CLOSE         : '\'' -> popMode;

// ==================== ELEM_CONTENT mode ====================
// Inside element content: raw text, enclosed expressions, nested elements

mode ELEM_CONTENT;

ELEM_CONTENT_CLOSE_TAG : '</' -> pushMode(END_TAG);
ELEM_CONTENT_OPEN_TAG  : '<' -> pushMode(START_TAG);
ELEM_CONTENT_LBRACE   : '{' -> pushMode(DEFAULT_MODE);
ELEM_CONTENT_ESCAPE_LBRACE : '{{';
ELEM_CONTENT_ESCAPE_RBRACE : '}}';
ElementContentChar     : ~[<{}]+;

// ==================== END_TAG mode ====================
// Inside a closing tag: </name>

mode END_TAG;

END_TAG_WS    : [ \t\r\n]+ -> skip;
END_TAG_CLOSE : '>' -> popMode;
END_TAG_QNAME : NameStartChar NameChar* (':' NameStartChar NameChar*)?;

// ==================== STRING_CONSTRUCTOR mode ====================
// Inside a string constructor: ``[content `{expr}` more]``

mode STRING_CONSTRUCTOR;

STRING_CONSTRUCTOR_INTERPOLATION_OPEN : '`{' -> pushMode(DEFAULT_MODE);
STRING_CONSTRUCTOR_CLOSE : ']``' -> popMode;
STRING_CONSTRUCTOR_BACKTICK : '`' ;
STRING_CONSTRUCTOR_CONTENT : (~[`\]])+ | ']' ~[`] (~[`\]])* ;

// ==================== Fragments ====================

fragment Digits
    : [0-9]+
    ;

fragment NameStartChar
    : [a-zA-Z_]
    | '\u00C0'..'\u00D6'
    | '\u00D8'..'\u00F6'
    | '\u00F8'..'\u02FF'
    | '\u0370'..'\u037D'
    | '\u037F'..'\u1FFF'
    | '\u200C'..'\u200D'
    | '\u2070'..'\u218F'
    | '\u2C00'..'\u2FEF'
    | '\u3001'..'\uD7FF'
    | '\uF900'..'\uFDCF'
    | '\uFDF0'..'\uFFFD'
    ;

fragment NameChar
    : NameStartChar
    | [0-9.\-]
    | '\u00B7'
    | '\u0300'..'\u036F'
    | '\u203F'..'\u2040'
    ;

fragment PredefinedEntityRef
    : '&' ('lt' | 'gt' | 'amp' | 'quot' | 'apos') ';'
    ;

fragment CharRef
    : '&#' [0-9]+ ';'
    | '&#x' [0-9a-fA-F]+ ';'
    ;
