parser grammar XQueryParser;

options { tokenVocab = XQueryLexer; }

// ==================== Module / Prolog ====================

module
    : versionDecl? (libraryModule | mainModule) EOF
    ;

versionDecl
    : KW_XQUERY KW_VERSION StringLiteral (KW_ENCODING StringLiteral)? SEMICOLON
    ;

libraryModule
    : moduleDecl prolog
    ;

moduleDecl
    : KW_MODULE KW_NAMESPACE ncName EQUALS StringLiteral SEMICOLON
    ;

mainModule
    : prolog queryBody
    ;

prolog
    : ((defaultNamespaceDecl | namespaceDecl | importDecl | optionDecl | varDecl | functionDecl | decimalFormatDecl | contextItemDecl) SEMICOLON)*
    ;

decimalFormatDecl
    : KW_DECLARE KW_DECIMAL_FORMAT eqName (ncName EQUALS StringLiteral)*
    | KW_DECLARE KW_DEFAULT KW_DECIMAL_FORMAT (ncName EQUALS StringLiteral)*
    ;

contextItemDecl
    : KW_DECLARE KW_CONTEXT KW_ITEM (KW_AS sequenceType)? (ASSIGN exprSingle | KW_EXTERNAL (ASSIGN exprSingle)?)
    ;

defaultNamespaceDecl
    : KW_DECLARE KW_DEFAULT (KW_ELEMENT | KW_FUNCTION) KW_NAMESPACE StringLiteral
    ;

namespaceDecl
    : KW_DECLARE KW_NAMESPACE ncName EQUALS StringLiteral
    ;

importDecl
    : KW_IMPORT (moduleImport | schemaImport)
    ;

moduleImport
    : KW_MODULE (KW_NAMESPACE ncName EQUALS)? StringLiteral
      (KW_AT StringLiteral (COMMA StringLiteral)*)?
    ;

schemaImport
    : KW_SCHEMA (KW_NAMESPACE ncName EQUALS | KW_DEFAULT KW_ELEMENT KW_NAMESPACE)?
      StringLiteral (KW_AT StringLiteral (COMMA StringLiteral)*)?
    ;

optionDecl
    : KW_DECLARE KW_OPTION eqName StringLiteral
    | KW_DECLARE KW_CONSTRUCTION (KW_PRESERVE | KW_STRIP)
    | KW_DECLARE KW_ORDERING (KW_ORDERED | KW_UNORDERED)
    | KW_DECLARE KW_DEFAULT KW_ORDER KW_EMPTY (KW_GREATEST | KW_LEAST)
    | KW_DECLARE KW_BOUNDARY_SPACE (KW_PRESERVE | KW_STRIP)
    | KW_DECLARE KW_COPY_NAMESPACES preserveMode COMMA inheritMode
    | KW_DECLARE KW_BASE_URI StringLiteral
    | KW_DECLARE KW_DEFAULT KW_COLLATION StringLiteral
    ;

preserveMode
    : KW_PRESERVE | KW_NO_PRESERVE
    ;

inheritMode
    : KW_INHERIT | KW_NO_INHERIT
    ;

annotation
    : PERCENT eqName (LPAREN literal (COMMA literal)* RPAREN)?
    ;

varDecl
    : KW_DECLARE annotation* KW_VARIABLE DOLLAR varName typeDeclaration?
      (ASSIGN exprSingle | KW_EXTERNAL (ASSIGN exprSingle)?)
    ;

functionDecl
    : KW_DECLARE annotation* KW_FUNCTION eqName LPAREN paramList? RPAREN
      (KW_AS sequenceType)?
      (enclosedExpr | KW_EXTERNAL)
    ;

paramList
    : param (COMMA param)*
    ;

param
    : DOLLAR varName typeDeclaration?
    ;

// ==================== Expressions ====================

queryBody
    : expr
    ;

expr
    : exprSingle (COMMA exprSingle)*
    ;

exprSingle
    : flworExpr
    | quantifiedExpr
    | switchExpr
    | typeswitchExpr
    | ifExpr
    | tryCatchExpr
    | insertExpr
    | deleteExpr
    | replaceExpr
    | renameExpr
    | transformExpr
    | orExpr
    ;

// ==================== FLWOR ====================

flworExpr
    : initialClause intermediateClause* KW_RETURN exprSingle (KW_OTHERWISE exprSingle)?
    ;

initialClause
    : forClause
    | letClause
    | windowClause
    ;

intermediateClause
    : forClause
    | letClause
    | whereClause
    | orderByClause
    | groupByClause
    | countClause
    | windowClause
    | whileClause
    ;

forClause
    : KW_FOR KW_MEMBER? forBinding (COMMA forBinding)*
    ;

forBinding
    : DOLLAR varName typeDeclaration? allowingEmpty? positionalVar? KW_IN exprSingle
    ;

allowingEmpty
    : KW_ALLOWING KW_EMPTY
    ;

positionalVar
    : KW_AT DOLLAR varName
    ;

letClause
    : KW_LET letBinding (COMMA letBinding)*
    ;

letBinding
    : DOLLAR varName typeDeclaration? ASSIGN exprSingle
    ;

whereClause
    : KW_WHERE exprSingle
    ;

orderByClause
    : (KW_STABLE KW_ORDER | KW_ORDER) KW_BY orderSpecList
    ;

orderSpecList
    : orderSpec (COMMA orderSpec)*
    ;

orderSpec
    : exprSingle orderDirection? emptyOrderSpec? collationSpec?
    ;

orderDirection
    : KW_ASCENDING
    | KW_DESCENDING
    ;

emptyOrderSpec
    : KW_EMPTY KW_GREATEST
    | KW_EMPTY KW_LEAST
    ;

collationSpec
    : KW_COLLATION StringLiteral
    ;

groupByClause
    : KW_GROUP KW_BY groupingSpecList
    ;

groupingSpecList
    : groupingSpec (COMMA groupingSpec)*
    ;

groupingSpec
    : DOLLAR varName (typeDeclaration? ASSIGN exprSingle)? collationSpec?
    ;

countClause
    : KW_COUNT DOLLAR varName
    ;

windowClause
    : KW_FOR (KW_TUMBLING | KW_SLIDING) KW_WINDOW
      DOLLAR varName typeDeclaration? KW_IN exprSingle
      windowStartCondition windowEndCondition?
    ;

windowStartCondition
    : KW_START windowVars KW_WHEN exprSingle
    ;

windowEndCondition
    : KW_ONLY? KW_END windowVars KW_WHEN exprSingle
    ;

windowVars
    : (DOLLAR varName)? positionalVarInWindow? previousVarInWindow? nextVarInWindow?
    ;

positionalVarInWindow
    : KW_AT DOLLAR varName
    ;

previousVarInWindow
    : KW_PREVIOUS DOLLAR varName
    ;

nextVarInWindow
    : KW_NEXT DOLLAR varName
    ;

// XQuery 4.0
whileClause
    : KW_WHILE LPAREN exprSingle RPAREN
    ;

// ==================== Conditionals ====================

ifExpr
    : KW_IF LPAREN expr RPAREN KW_THEN exprSingle KW_ELSE exprSingle     # IfThenElse
    | KW_IF LPAREN expr RPAREN LBRACE expr RBRACE                         # BracedIf
    ;

quantifiedExpr
    : (KW_SOME | KW_EVERY) quantifiedBinding (COMMA quantifiedBinding)* KW_SATISFIES exprSingle
    ;

quantifiedBinding
    : DOLLAR varName typeDeclaration? KW_IN exprSingle
    ;

switchExpr
    : KW_SWITCH LPAREN expr RPAREN switchCaseClause+ KW_DEFAULT KW_RETURN exprSingle
    ;

switchCaseClause
    : (KW_CASE switchCaseOperand)+ KW_RETURN exprSingle
    ;

switchCaseOperand
    : exprSingle
    ;

typeswitchExpr
    : KW_TYPESWITCH LPAREN expr RPAREN typeswitchCaseClause+ KW_DEFAULT (DOLLAR varName)? KW_RETURN exprSingle
    ;

typeswitchCaseClause
    : KW_CASE (DOLLAR varName KW_AS)? sequenceTypeUnion KW_RETURN exprSingle
    ;

sequenceTypeUnion
    : sequenceType (PIPE sequenceType)*
    ;

tryCatchExpr
    : KW_TRY enclosedExpr catchClause+
    ;

catchClause
    : KW_CATCH catchErrorList enclosedExpr
    ;

catchErrorList
    : nameTest (PIPE nameTest)*
    ;

// ==================== Operator Precedence Chain ====================

orExpr
    : andExpr (KW_OR andExpr)*
    ;

andExpr
    : notExpr (KW_AND notExpr)*
    ;

// XQuery 4.0: not expr
notExpr
    : KW_NOT? comparisonExpr
    ;

comparisonExpr
    : ftContainsExpr (compOp ftContainsExpr)?
    ;

// XQuery Full-Text: contains text expression
ftContainsExpr
    : otherwiseExpr (KW_CONTAINS KW_TEXT ftSelection ftMatchOptions?)?
    ;

compOp
    : KW_EQ | KW_NE | KW_LT | KW_LE | KW_GT | KW_GE           // value comparison
    | EQUALS | NOT_EQUALS | LESS_THAN | LESS_EQ                  // general comparison
    | GREATER_THAN | GREATER_EQ
    | KW_IS | LSHIFT | RSHIFT                                    // node comparison
    ;

// XQuery 4.0
otherwiseExpr
    : stringConcatExpr (KW_OTHERWISE stringConcatExpr)*
    ;

stringConcatExpr
    : rangeExpr (PIPEPIPE rangeExpr)*
    ;

rangeExpr
    : additiveExpr (KW_TO additiveExpr)?
    ;

additiveExpr
    : multiplicativeExpr ((PLUS | MINUS) multiplicativeExpr)*
    ;

multiplicativeExpr
    : unionExpr ((STAR | KW_DIV | KW_IDIV | KW_MOD) unionExpr)*
    ;

unionExpr
    : intersectExceptExpr ((KW_UNION | PIPE) intersectExceptExpr)*
    ;

intersectExceptExpr
    : instanceofExpr ((KW_INTERSECT | KW_EXCEPT) instanceofExpr)*
    ;

instanceofExpr
    : treatExpr (KW_INSTANCE KW_OF sequenceType)?
    ;

treatExpr
    : castableExpr (KW_TREAT KW_AS sequenceType)?
    ;

castableExpr
    : castExpr (KW_CASTABLE KW_AS singleType)?
    ;

castExpr
    : arrowExpr (KW_CAST KW_AS singleType)?
    ;

arrowExpr
    : unaryExpr (arrowOp arrowFunctionSpecifier argumentList)*
    ;

arrowOp
    : FAT_ARROW
    | THIN_ARROW
    ;

arrowFunctionSpecifier
    : eqName
    | varRef
    | parenthesizedExpr
    ;

unaryExpr
    : (MINUS | PLUS)* simpleMapExpr
    ;

simpleMapExpr
    : pathExpr (BANG pathExpr)*
    ;

// ==================== Path / Step Expressions ====================

pathExpr
    : SLASH relativePathExpr?    # RootedPath
    | DSLASH relativePathExpr    # DescendantPath
    | relativePathExpr           # RelativePath
    ;

relativePathExpr
    : stepExpr ((SLASH | DSLASH) stepExpr)*
    ;

stepExpr
    : axisStep
    | postfixExpr
    ;

axisStep
    : (forwardStep | reverseStep) predicateList
    ;

forwardStep
    : forwardAxis nodeTest
    | abbrevForwardStep
    ;

forwardAxis
    : KW_CHILD COLONCOLON
    | KW_DESCENDANT COLONCOLON
    | KW_ATTRIBUTE COLONCOLON
    | KW_SELF COLONCOLON
    | KW_DESCENDANT_OR_SELF COLONCOLON
    | KW_FOLLOWING_SIBLING COLONCOLON
    | KW_FOLLOWING COLONCOLON
    | KW_NAMESPACE COLONCOLON
    ;

abbrevForwardStep
    : AT_SIGN? nodeTest
    ;

reverseStep
    : reverseAxis nodeTest
    | abbreviatedReverseStep
    ;

reverseAxis
    : KW_PARENT COLONCOLON
    | KW_ANCESTOR COLONCOLON
    | KW_PRECEDING_SIBLING COLONCOLON
    | KW_PRECEDING COLONCOLON
    | KW_ANCESTOR_OR_SELF COLONCOLON
    ;

abbreviatedReverseStep
    : DOTDOT
    ;

nodeTest
    : kindTest
    | nameTest
    ;

nameTest
    : eqName
    | wildcard
    ;

wildcard
    : STAR                          # WildcardAll
    | ncName COLON STAR             # WildcardLocalAll
    | STAR COLON ncName             # WildcardNsAll
    | BracedURILiteral STAR         # WildcardBracedUriAll
    ;

// ==================== Postfix / Primary ====================

postfixExpr
    : primaryExpr (predicate | argumentList | lookup)*
    ;

primaryExpr
    : literal
    | varRef
    | parenthesizedExpr
    | contextItemExpr
    | functionCall
    | namedFunctionRef
    | inlineFunctionExpr
    | orderedExpr
    | unorderedExpr
    | mapConstructor
    | arrayConstructor
    | recordConstructor
    | stringConstructor
    | dirElemConstructor
    | compConstructor
    | unaryLookup
    ;

unaryLookup
    : QUESTION keySpecifier
    ;

literal
    : IntegerLiteral
    | DecimalLiteral
    | DoubleLiteral
    | StringLiteral
    ;

varRef
    : DOLLAR varName
    ;

varName
    : eqName
    ;

parenthesizedExpr
    : LPAREN expr? RPAREN
    ;

contextItemExpr
    : DOT
    ;

functionCall
    : eqName argumentList     // must not be a reserved function name
    ;

argumentList
    : LPAREN (argument (COMMA argument)*)? RPAREN
    ;

argument
    : ncName ASSIGN exprSingle   // XPath 4.0: keyword argument (name := value)
    | exprSingle
    | QUESTION  // ArgumentPlaceholder for partial application
    ;

namedFunctionRef
    : eqName HASH IntegerLiteral
    ;

inlineFunctionExpr
    : KW_FUNCTION LPAREN paramList? RPAREN (KW_AS sequenceType)? enclosedExpr
    | THIN_ARROW DOLLAR varName LBRACE expr RBRACE                              // 4.0 shorthand
    ;

orderedExpr
    : KW_ORDERED enclosedExpr
    ;

unorderedExpr
    : KW_UNORDERED enclosedExpr
    ;

enclosedExpr
    : LBRACE expr? RBRACE
    ;

// ==================== Constructors ====================

// Direct element constructor (uses lexer modes via XQueryLexerAdapter)
dirElemConstructor
    : LESS_THAN startTagBody
      ( START_TAG_EMPTY_CLOSE                                                          // self-closing: <br/>
      | START_TAG_CLOSE dirElemContent* ELEM_CONTENT_CLOSE_TAG endTagName END_TAG_CLOSE  // <tag>...</tag>
      )
    ;

startTagBody
    : START_TAG_QNAME dirAttribute*
    ;

dirAttribute
    : START_TAG_QNAME START_TAG_EQUALS
      ( START_TAG_STRING                                                                  // simple: attr="value"
      | ATTR_VALUE_DQ_OPEN dirAttrValueContent* ATTR_DQ_CLOSE                             // enclosed: attr="text {expr} text"
      | ATTR_VALUE_SQ_OPEN dirAttrValueContentSq* ATTR_SQ_CLOSE                           // enclosed: attr='text {expr} text'
      )
    ;

dirAttrValueContent
    : ATTR_DQ_CHAR                                                                        // literal text
    | ATTR_DQ_ESCAPE_LBRACE                                                               // {{ literal brace
    | ATTR_DQ_ESCAPE_RBRACE                                                               // }} literal brace
    | ATTR_DQ_LBRACE expr RBRACE                                                          // {expr} enclosed expression
    ;

dirAttrValueContentSq
    : ATTR_SQ_CHAR
    | ATTR_SQ_ESCAPE_LBRACE
    | ATTR_SQ_ESCAPE_RBRACE
    | ATTR_SQ_LBRACE expr RBRACE
    ;

dirElemContent
    : dirElemConstructor                                                               // nested <child>
    | ELEM_CONTENT_OPEN_TAG startTagBody
      ( START_TAG_EMPTY_CLOSE                                                          // nested self-closing
      | START_TAG_CLOSE dirElemContent* ELEM_CONTENT_CLOSE_TAG endTagName END_TAG_CLOSE  // nested open/close
      )
    | dirEnclosedExpr                                                                  // {expr}
    | ElementContentChar                                                               // raw text
    | ELEM_CONTENT_ESCAPE_LBRACE                                                       // {{ literal
    | ELEM_CONTENT_ESCAPE_RBRACE                                                       // }} literal
    ;

dirEnclosedExpr
    : ELEM_CONTENT_LBRACE expr? RBRACE
    ;

endTagName
    : END_TAG_QNAME
    ;

// Computed constructors
compConstructor
    : compDocConstructor
    | compElemConstructor
    | compAttrConstructor
    | compTextConstructor
    | compCommentConstructor
    | compPIConstructor
    | compNamespaceConstructor
    ;

compDocConstructor
    : KW_DOCUMENT enclosedExpr
    ;

compElemConstructor
    : KW_ELEMENT (eqName | enclosedExpr) enclosedExpr
    ;

compAttrConstructor
    : KW_ATTRIBUTE (eqName | enclosedExpr) enclosedExpr
    ;

compTextConstructor
    : KW_TEXT enclosedExpr
    ;

compCommentConstructor
    : KW_COMMENT enclosedExpr
    ;

compPIConstructor
    : KW_PROCESSING_INSTRUCTION (ncName | enclosedExpr) enclosedExpr
    ;

compNamespaceConstructor
    : KW_NAMESPACE (ncName | enclosedExpr) enclosedExpr
    ;

// ==================== Map / Array ====================

mapConstructor
    : KW_MAP LBRACE (mapConstructorEntry (COMMA mapConstructorEntry)*)? RBRACE
    | LBRACE mapConstructorEntry (COMMA mapConstructorEntry)* RBRACE
    ;

mapConstructorEntry
    : exprSingle COLON exprSingle
    ;

arrayConstructor
    : squareArrayConstructor
    | curlyArrayConstructor
    ;

squareArrayConstructor
    : LBRACKET (exprSingle (COMMA exprSingle)*)? RBRACKET
    ;

curlyArrayConstructor
    : KW_ARRAY enclosedExpr
    ;

// XPath 4.0: record { name: value, ... }
recordConstructor
    : KW_RECORD LBRACE (recordConstructorEntry (COMMA recordConstructorEntry)*)? RBRACE
    ;

recordConstructorEntry
    : ncName COLON exprSingle
    ;

// XQuery 3.1/4.0: String constructor ``[content `{expr}` more]``
stringConstructor
    : STRING_CONSTRUCTOR_OPEN stringConstructorContent* STRING_CONSTRUCTOR_CLOSE
    ;

stringConstructorContent
    : STRING_CONSTRUCTOR_CONTENT                                    // literal text
    | STRING_CONSTRUCTOR_INTERPOLATION_OPEN expr RBRACE STRING_CONSTRUCTOR_BACKTICK  // `{expr}`
    | STRING_CONSTRUCTOR_BACKTICK                                   // literal backtick in content
    ;

// Lookup
lookup
    : QUESTION keySpecifier
    ;

keySpecifier
    : ncName
    | IntegerLiteral
    | parenthesizedExpr
    | STAR
    ;

// ==================== Predicates ====================

predicateList
    : predicate*
    ;

predicate
    : LBRACKET expr RBRACKET
    ;

// ==================== Types ====================

typeDeclaration
    : KW_AS sequenceType
    ;

sequenceType
    : KW_EMPTY_SEQUENCE LPAREN RPAREN     # EmptySequenceType
    | itemType occurrenceIndicator?        # ItemSequenceType
    ;

itemType
    : KW_ITEM LPAREN RPAREN
    | kindTest
    | atomicOrUnionType
    | KW_FUNCTION LPAREN STAR RPAREN                                                        // function(*) wildcard
    | KW_FUNCTION LPAREN (sequenceType (COMMA sequenceType)*)? RPAREN KW_AS sequenceType   // function type
    | KW_MAP LPAREN STAR RPAREN                                                             // map(*) wildcard
    | KW_MAP LPAREN atomicOrUnionType COMMA sequenceType RPAREN                             // map type
    | KW_ARRAY LPAREN STAR RPAREN                                                           // array(*) wildcard
    | KW_ARRAY LPAREN sequenceType RPAREN                                                   // array type
    | KW_RECORD LPAREN recordFieldDecl (COMMA recordFieldDecl)* (COMMA STAR)? RPAREN       // record type (4.0)
    | KW_RECORD LPAREN STAR RPAREN                                                          // record(*) wildcard (4.0)
    | KW_ENUM LPAREN StringLiteral (COMMA StringLiteral)* RPAREN                            // enum type (4.0)
    | KW_UNION LPAREN sequenceType (COMMA sequenceType)+ RPAREN                            // union type (4.0)
    | parenthesizedItemType
    ;

recordFieldDecl
    : ncName QUESTION? (KW_AS sequenceType)?    // field-name?  as type
    ;

atomicOrUnionType
    : eqName
    ;

parenthesizedItemType
    : LPAREN itemType RPAREN
    ;

singleType
    : atomicOrUnionType QUESTION?
    ;

occurrenceIndicator
    : QUESTION
    | STAR
    | PLUS
    ;

kindTest
    : documentTest
    | elementTest
    | attributeTest
    | schemaElementTest
    | schemaAttributeTest
    | piTest
    | commentTest
    | textTest
    | namespaceNodeTest
    | anyKindTest
    ;

anyKindTest
    : KW_NODE LPAREN RPAREN
    ;

documentTest
    : KW_DOCUMENT_NODE LPAREN (elementTest | schemaElementTest)? RPAREN
    ;

elementTest
    : KW_ELEMENT LPAREN ((eqName | STAR) (COMMA eqName QUESTION?)?)? RPAREN
    ;

attributeTest
    : KW_ATTRIBUTE LPAREN ((eqName | STAR) (COMMA eqName)?)? RPAREN
    ;

schemaElementTest
    : KW_SCHEMA_ELEMENT LPAREN eqName RPAREN
    ;

schemaAttributeTest
    : KW_SCHEMA_ATTRIBUTE LPAREN eqName RPAREN
    ;

piTest
    : KW_PROCESSING_INSTRUCTION LPAREN (ncName | StringLiteral)? RPAREN
    ;

commentTest
    : KW_COMMENT LPAREN RPAREN
    ;

textTest
    : KW_TEXT LPAREN RPAREN
    ;

namespaceNodeTest
    : KW_NAMESPACE_NODE LPAREN RPAREN
    ;

// ==================== Names ====================

// EQName: either a prefixed name or an unprefixed NCName or URIQualifiedName
eqName
    : qName
    | URIQualifiedName
    ;

qName
    : prefixedName
    | unprefixedName
    ;

prefixedName
    : ncName COLON ncName
    ;

unprefixedName
    : ncName
    ;

// ==================== XQuery Full-Text ====================

ftSelection
    : ftOr ftPosFilter*
    ;

ftOr
    : ftAnd (KW_FTOR ftAnd)*
    ;

ftAnd
    : ftMildNot (KW_FTAND ftMildNot)*
    ;

ftMildNot
    : ftUnaryNot (KW_NOT KW_IN ftUnaryNot)*
    ;

ftUnaryNot
    : KW_FTNOT? ftPrimary
    ;

ftPrimary
    : ftWords ftAnyAllOption?                    // word/phrase match
    | LPAREN ftSelection RPAREN                  // grouped selection
    ;

ftWords
    : ftWordsValue
    ;

ftWordsValue
    : StringLiteral                              // literal string to search for
    | LBRACE expr RBRACE                         // computed search string
    ;

ftAnyAllOption
    : KW_ANY KW_WORD?
    | KW_ALL KW_WORDS?
    | KW_PHRASE
    ;

ftPosFilter
    : KW_ORDERED                                 // terms must appear in order
    | KW_WINDOW IntegerLiteral KW_WORDS          // terms within N words
    | KW_DISTANCE IntegerLiteral KW_WORDS        // exact distance between terms
    | KW_SAME KW_SENTENCE                        // within same sentence
    | KW_SAME KW_PARAGRAPH                       // within same paragraph
    | KW_AT KW_START                             // at document/element start
    | KW_AT KW_END                               // at document/element end
    | KW_ENTIRE KW_CONTENT                       // matches entire content
    ;

ftMatchOptions
    : ftMatchOption+
    ;

ftMatchOption
    : KW_USING KW_STEMMING                       // enable stemming
    | KW_USING KW_NO KW_STEMMING                 // disable stemming
    | KW_USING KW_LANGUAGE StringLiteral          // specify language
    | KW_USING KW_WILDCARDS                       // enable wildcards (. = any char, .* = any)
    | KW_USING KW_NO KW_WILDCARDS                 // disable wildcards
    | KW_USING KW_CASE KW_SENSITIVE               // case-sensitive matching
    | KW_USING KW_CASE KW_INSENSITIVE             // case-insensitive matching
    | KW_USING KW_DIACRITICS KW_SENSITIVE          // diacritic-sensitive
    | KW_USING KW_DIACRITICS KW_INSENSITIVE        // diacritic-insensitive
    | KW_USING KW_STOP KW_WORDS LPAREN StringLiteral (COMMA StringLiteral)* RPAREN  // custom stop words
    | KW_USING KW_NO KW_STOP KW_WORDS             // no stop words
    | KW_USING KW_THESAURUS StringLiteral          // thesaurus file
    ;

// ==================== XQuery Update Facility ====================

insertExpr
    : KW_INSERT KW_NODE exprSingle KW_INTO exprSingle                                      # InsertInto
    | KW_INSERT KW_NODE exprSingle KW_AS KW_FIRST KW_INTO exprSingle                       # InsertAsFirst
    | KW_INSERT KW_NODE exprSingle KW_AS KW_LAST KW_INTO exprSingle                        # InsertAsLast
    | KW_INSERT KW_NODE exprSingle KW_BEFORE exprSingle                                    # InsertBefore
    | KW_INSERT KW_NODE exprSingle KW_AFTER exprSingle                                     # InsertAfter
    | KW_INSERT KW_NODES exprSingle KW_INTO exprSingle                                     # InsertNodesInto
    | KW_INSERT KW_NODES exprSingle KW_AS KW_FIRST KW_INTO exprSingle                      # InsertNodesAsFirst
    | KW_INSERT KW_NODES exprSingle KW_AS KW_LAST KW_INTO exprSingle                       # InsertNodesAsLast
    | KW_INSERT KW_NODES exprSingle KW_BEFORE exprSingle                                   # InsertNodesBefore
    | KW_INSERT KW_NODES exprSingle KW_AFTER exprSingle                                    # InsertNodesAfter
    ;

deleteExpr
    : KW_DELETE KW_NODE exprSingle                                                          # DeleteNode
    | KW_DELETE KW_NODES exprSingle                                                         # DeleteNodes
    ;

replaceExpr
    : KW_REPLACE KW_NODE exprSingle KW_WITH exprSingle                                     # ReplaceNode
    | KW_REPLACE KW_VALUE KW_OF KW_NODE exprSingle KW_WITH exprSingle                      # ReplaceValue
    ;

renameExpr
    : KW_RENAME KW_NODE exprSingle KW_AS exprSingle
    ;

transformExpr
    : KW_COPY DOLLAR varName ASSIGN exprSingle
      (COMMA DOLLAR varName ASSIGN exprSingle)*
      KW_MODIFY exprSingle KW_RETURN exprSingle
    ;

// NCName can be a bare identifier or a keyword used as a name.
// This is how XQuery handles keyword/name disambiguation.
ncName
    : NCName
    | KW_FOR | KW_LET | KW_WHERE | KW_ORDER | KW_BY | KW_RETURN
    | KW_IN | KW_ALLOWING | KW_EMPTY | KW_AT | KW_STABLE
    | KW_ASCENDING | KW_DESCENDING | KW_GREATEST | KW_LEAST | KW_COLLATION
    | KW_GROUP | KW_COUNT | KW_WHILE | KW_MEMBER | KW_RECORD | KW_ENUM
    | KW_TUMBLING | KW_SLIDING | KW_WINDOW | KW_START | KW_END | KW_WHEN
    | KW_ONLY | KW_PREVIOUS | KW_NEXT | KW_CURRENT
    | KW_SOME | KW_EVERY | KW_SATISFIES
    | KW_SWITCH | KW_CASE | KW_DEFAULT | KW_TYPESWITCH
    | KW_TRY | KW_CATCH
    | KW_AND | KW_OR | KW_NOT
    | KW_EQ | KW_NE | KW_LT | KW_LE | KW_GT | KW_GE | KW_IS
    | KW_DIV | KW_IDIV | KW_MOD
    | KW_INSTANCE | KW_OF | KW_TREAT | KW_AS | KW_CASTABLE | KW_CAST
    | KW_TO | KW_UNION | KW_INTERSECT | KW_EXCEPT | KW_OTHERWISE
    | KW_ITEM | KW_NODE | KW_ELEMENT | KW_ATTRIBUTE | KW_TEXT | KW_COMMENT
    | KW_DOCUMENT_NODE | KW_PROCESSING_INSTRUCTION
    | KW_SCHEMA_ELEMENT | KW_SCHEMA_ATTRIBUTE | KW_NAMESPACE_NODE
    | KW_EMPTY_SEQUENCE | KW_FUNCTION
    | KW_CHILD | KW_DESCENDANT | KW_SELF | KW_DESCENDANT_OR_SELF
    | KW_FOLLOWING_SIBLING | KW_FOLLOWING
    | KW_PARENT | KW_ANCESTOR | KW_PRECEDING_SIBLING | KW_PRECEDING | KW_ANCESTOR_OR_SELF
    | KW_NAMESPACE | KW_DOCUMENT | KW_MAP | KW_ARRAY
    | KW_XQUERY | KW_VERSION | KW_ENCODING | KW_MODULE | KW_DECLARE
    | KW_VARIABLE | KW_IMPORT | KW_SCHEMA | KW_EXTERNAL | KW_OPTION
    | KW_CONSTRUCTION | KW_ORDERING | KW_ORDERED | KW_UNORDERED
    | KW_PRESERVE | KW_STRIP | KW_BOUNDARY_SPACE | KW_COPY_NAMESPACES
    | KW_NO_PRESERVE | KW_INHERIT | KW_NO_INHERIT | KW_BASE_URI
    | KW_IF | KW_THEN | KW_ELSE
    | KW_INSERT | KW_DELETE | KW_REPLACE | KW_RENAME | KW_COPY | KW_MODIFY
    | KW_CONTEXT | KW_DECIMAL_FORMAT
    | KW_INTO | KW_AFTER | KW_BEFORE | KW_FIRST | KW_LAST | KW_VALUE | KW_WITH | KW_NODES
    | KW_CONTAINS | KW_FTAND | KW_FTOR | KW_FTNOT | KW_PHRASE | KW_ANY | KW_ALL
    | KW_WORD | KW_WORDS | KW_DISTANCE | KW_SAME | KW_SENTENCE | KW_PARAGRAPH
    | KW_ENTIRE | KW_CONTENT | KW_STEMMING | KW_LANGUAGE | KW_WILDCARDS
    | KW_SENSITIVE | KW_INSENSITIVE | KW_DIACRITICS | KW_STOP | KW_THESAURUS
    | KW_USING | KW_NO
    ;
