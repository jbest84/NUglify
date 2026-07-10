# Requirements Document

## Introduction

This feature adds support for CSS Nesting to the NUglify CSS parser and minifier, following the [W3C CSS Nesting Module Level 1](https://www.w3.org/TR/css-nesting-1/) specification. CSS Nesting allows style rules to be nested inside other style rules, so that the selector of the inner rule is interpreted relative to the selector of the outer rule.

Today the NUglify CSS parser (`CssParser`) treats the body of a declaration block as a list of declarations only (see `ParseDeclarationList` / `ParseDeclaration`), and the scanner (`CssScanner`) does not tokenize the nesting selector `&`. This feature extends both the scanner and the parser so that nested style rules are recognized, parsed, and re-emitted correctly in both minified and pretty output modes, while preserving existing (non-nested) behavior.

The core capabilities from the specification that must be supported are:

- The nesting selector `&`, referring to the parent rule's selector.
- Using `&` standalone, in compound selectors (`&.bar`), multiple times in one selector, and in positions other than the start (`.parent &`).
- Relative nested selectors where a leading `&` is implied when a nested selector begins with a combinator (`> .baz`, `+ .bar`, `~ .qux`) or with a type/class/id/pseudo/attribute selector.
- Nested selector lists where every selector in the list is relative to the parent.
- Doubling the nesting selector (`&&`).
- Arbitrarily complex parent selectors.
- Multiple levels of nesting.
- Nesting inside conditional and grouping at-rules (`@media`, `@supports`, `@layer`, `@scope`).
- Correct minification/output of all of the above.

## Glossary

- **CSS_Parser**: The `NUglify.Css.CssParser` component that consumes tokens and produces minified or pretty CSS output.
- **CSS_Scanner**: The `NUglify.Css.CssScanner` component that converts source text into tokens consumed by the CSS_Parser.
- **Nesting_Selector**: The `&` token, which represents the selector of the immediately enclosing (parent) style rule.
- **Parent_Rule**: A style rule that contains one or more nested style rules within its declaration block.
- **Nested_Rule**: A style rule that appears inside the declaration block of another style rule.
- **Nested_Selector**: The selector of a Nested_Rule.
- **Relative_Nested_Selector**: A Nested_Selector that begins with a combinator (`>`, `+`, `~`) or with a compound selector that does not start with `&`, for which a leading `&` is implied.
- **Declaration_Block**: The `{ ... }` body of a style rule, which under this feature may contain both declarations and Nested_Rules.
- **Combinator**: A CSS combinator token: descendant (whitespace), child (`>`), next-sibling (`+`), or subsequent-sibling (`~`).
- **At_Rule_Block**: The `{ ... }` body of a conditional or grouping at-rule such as `@media`, `@supports`, `@layer`, or `@scope`.
- **Minified_Output**: Output produced by the CSS_Parser using the default (single-line) `OutputMode`.
- **Pretty_Output**: Output produced by the CSS_Parser using `CssSettings.Pretty()` (multiple-line `OutputMode`).

## Requirements

### Requirement 1: Tokenize the nesting selector

**User Story:** As a developer using NUglify, I want the CSS_Scanner to recognize the `&` nesting selector, so that nested rules using `&` can be parsed instead of raising scanning errors.

#### Acceptance Criteria

1. WHEN the CSS_Scanner encounters an unescaped `&` character while scanning selector text (that is, a `&` that is not inside a string literal, a comment, or a property declaration value), THE CSS_Scanner SHALL produce a single distinct token representing the Nesting_Selector for that `&` character.
2. WHEN the CSS_Scanner encounters two consecutive unescaped `&` characters in selector text, THE CSS_Scanner SHALL produce exactly two consecutive Nesting_Selector tokens, one per `&` character, with no combined or merged token.
3. WHEN the CSS_Scanner encounters a Nesting_Selector token immediately adjacent to another selector token with no intervening whitespace (such as `&.bar` or `.parent&`), THE CSS_Scanner SHALL emit the Nesting_Selector token as a separate token from the adjacent token and preserve the absence of whitespace between them.
4. WHERE a `&` character appears inside a string literal or a comment, THE CSS_Scanner SHALL prioritize the literal context and treat the `&` as part of that string literal or comment rather than as a Nesting_Selector token.
5. IF the CSS_Scanner encounters an unescaped `&` character in a position where a Nesting_Selector token is not valid (such as within a property declaration value), THEN THE CSS_Scanner SHALL report a scanning error identifying the location of the offending `&` character rather than silently discarding it.

### Requirement 2: Parse nested style rules within a declaration block

**User Story:** As a developer, I want the CSS_Parser to parse style rules nested inside a declaration block, so that nested CSS is accepted and re-emitted correctly.

#### Acceptance Criteria

1. WHEN, inside a Parent_Rule's Declaration_Block, the CSS_Parser encounters a Nested_Selector immediately followed by an opening brace that begins a Declaration_Block, THE CSS_Parser SHALL parse the enclosed content as a Nested_Rule rather than as a declaration.
2. WHILE parsing a Declaration_Block, THE CSS_Parser SHALL accept a mixture of declarations and Nested_Rules in source order.
3. WHEN a Declaration_Block contains both declarations and Nested_Rules, THE CSS_Parser SHALL preserve the relative source order of declarations and Nested_Rules in the output.
4. IF the CSS_Parser cannot preserve the relative source order of declarations and Nested_Rules within a Declaration_Block, THEN THE CSS_Parser SHALL fail parsing and report a parse error rather than emit reordered output.
5. IF a construct encountered WHILE parsing a Declaration_Block matches neither a valid declaration nor a valid Nested_Rule, THEN THE CSS_Parser SHALL report a parse error identifying the offending token by its source position and SHALL reject the enclosing rule rather than emit partial output for that Declaration_Block.
6. IF the CSS_Parser reaches the end of input while a Nested_Rule's Declaration_Block remains unterminated by a closing brace, THEN THE CSS_Parser SHALL report a parse error identifying the source position of the unterminated Declaration_Block rather than emit the incomplete Nested_Rule.

### Requirement 3: Support the nesting selector in all valid positions

**User Story:** As a developer, I want `&` to be usable standalone, within compound selectors, multiple times, and in non-leading positions, so that all nesting patterns from the specification are supported.

#### Acceptance Criteria

1. WHEN a Nested_Selector consists of a standalone `&`, THE CSS_Parser SHALL parse the Nested_Rule and emit exactly one `&` token as the Nested_Selector with no additional characters or whitespace added.
2. WHEN a Nested_Selector contains `&` joined with a compound selector such as `&.bar` or `&:hover`, THE CSS_Parser SHALL parse the Nested_Rule and emit the combined selector with zero whitespace characters between `&` and the adjoining compound selector in both Minified_Output and Pretty_Output.
3. WHEN a Nested_Selector contains the Nesting_Selector two or more times, such as `& + &`, THE CSS_Parser SHALL parse the Nested_Rule and emit every occurrence of `&` in the same source order and with the same Combinators between occurrences as in the source.
4. WHEN a Nested_Selector places `&` after another selector, such as `.parent &`, THE CSS_Parser SHALL parse the Nested_Rule and emit `&` after the other selector, preserving the Combinator that separates them (a single space for a descendant Combinator, and the exact `>`, `+`, or `~` token for other Combinators).
5. WHEN a Nested_Selector contains two consecutive Nesting_Selectors (`&&`), THE CSS_Parser SHALL parse the Nested_Rule and emit both Nesting_Selectors adjacently with zero whitespace characters between them.
6. IF a Nesting_Selector appears in a position where a selector is not permitted, such as within a declaration value, THEN THE CSS_Parser SHALL fail parsing and report a parse error identifying the offending token rather than emit the Nesting_Selector.

### Requirement 4: Support relative nested selectors with an implied leading nesting selector

**User Story:** As a developer, I want nested selectors that begin with a combinator or a bare compound selector to be accepted, so that I can write relative selectors as described in the specification.

#### Acceptance Criteria

1. WHEN a Nested_Selector begins with a child (`>`), next-sibling (`+`), or subsequent-sibling (`~`) Combinator, THE CSS_Parser SHALL parse the enclosed content as a Relative_Nested_Selector Nested_Rule with an implied leading Nesting_Selector.
2. WHEN a Nested_Selector begins with a type, class, id, attribute, pseudo-class, or pseudo-element selector rather than `&`, THE CSS_Parser SHALL parse the enclosed content as a Relative_Nested_Selector Nested_Rule with an implied leading Nesting_Selector.
3. WHEN the CSS_Parser emits a Relative_Nested_Selector that began with a leading Combinator, THE CSS_Parser SHALL preserve that Combinator in the output and SHALL NOT insert an explicit `&` before the Combinator.
4. WHEN the CSS_Parser emits a Relative_Nested_Selector that began with a bare compound selector, THE CSS_Parser SHALL emit that compound selector unchanged and SHALL NOT insert an explicit `&`.
5. IF a Nested_Selector begins with a leading Combinator that is not followed by a valid compound selector, THEN THE CSS_Parser SHALL fail parsing and report a parse error identifying the offending token rather than emit the Nested_Rule.

### Requirement 5: Support nested selector lists

**User Story:** As a developer, I want a comma-separated list of nested selectors to be accepted, so that multiple selectors can share one nested declaration block.

#### Acceptance Criteria

1. WHEN a Nested_Rule uses a comma-separated list of two or more Nested_Selectors, THE CSS_Parser SHALL parse every selector in the list, allow optional whitespace around the commas, and associate the shared Declaration_Block with each parsed selector.
2. WHEN the CSS_Parser emits a nested selector list in Minified_Output, THE CSS_Parser SHALL separate the selectors with a single comma and no surrounding whitespace.
3. WHEN the CSS_Parser emits a nested selector list in Pretty_Output, THE CSS_Parser SHALL separate the selectors with a comma using the same selector-list formatting it applies to non-nested rules.
4. WHEN a nested selector list combines Relative_Nested_Selectors and selectors containing `&`, THE CSS_Parser SHALL parse and emit each selector in the list in source order.
5. IF any selector in a nested selector list has invalid syntax, THEN THE CSS_Parser SHALL fail the entire list, emit none of the selectors, and report a parse error indicating the invalid selector.
6. IF a nested selector list contains an empty selector position caused by a leading comma, a trailing comma, or two consecutive commas, THEN THE CSS_Parser SHALL fail the entire list and report a parse error rather than emit the non-empty selectors.

### Requirement 6: Support arbitrarily complex parent and nested selectors

**User Story:** As a developer, I want complex parent selectors and deep nesting to be supported, so that real-world stylesheets parse correctly.

#### Acceptance Criteria

1. WHERE a Parent_Rule uses a complex selector consisting of one or more compound selectors joined by one or more Combinators, THE CSS_Parser SHALL parse every Nested_Rule within that Parent_Rule's Declaration_Block.
2. WHEN a Nested_Rule itself contains further Nested_Rules, THE CSS_Parser SHALL parse each level of nesting to a depth of at least 64 levels.
3. WHILE memory remains available, WHEN a stylesheet nests Nested_Rules beyond 64 levels, THE CSS_Parser SHALL continue parsing each additional level rather than imposing a fixed maximum nesting depth.
4. WHEN the CSS_Parser emits multiple levels of Nested_Rules, THE CSS_Parser SHALL preserve the nesting structure so that the emitted nesting depth and each Nested_Rule's association with its immediate Parent_Rule match the source.
5. IF the CSS_Parser cannot preserve the nesting structure of a stylesheet, THEN THE CSS_Parser SHALL reject the entire stylesheet, emit no partial or flattened output, and report a parse error.

### Requirement 7: Support nesting inside at-rules

**User Story:** As a developer, I want nesting to work inside conditional and grouping at-rules, so that I can nest style rules within `@media`, `@supports`, `@layer`, and `@scope` blocks.

#### Acceptance Criteria

1. WHEN a style rule inside an At_Rule_Block for `@media` or `@supports` contains Nested_Rules, THE CSS_Parser SHALL parse the Nested_Rules within that style rule.
2. WHEN an At_Rule_Block for `@layer` or `@scope` contains style rules with Nested_Rules, THE CSS_Parser SHALL parse the Nested_Rules within those style rules.
3. WHEN a Nested_Rule appears directly inside a Parent_Rule and its body is an at-rule that is one of `@media`, `@supports`, `@layer`, or `@scope`, THE CSS_Parser SHALL parse the nested at-rule and the style rules within it.
4. WHEN the CSS_Parser emits Nested_Rules that appear inside an At_Rule_Block, in both Minified_Output and Pretty_Output, THE CSS_Parser SHALL preserve the containment of those Nested_Rules within the braces of that At_Rule_Block.
5. WHEN a Nested_Rule whose body is one of the at-rules `@media`, `@supports`, `@layer`, or `@scope` itself contains style rules with further Nested_Rules, THE CSS_Parser SHALL parse each level of nesting to the depth present in the source and preserve the containment of each Nested_Rule within its enclosing block.
6. IF, while parsing an At_Rule_Block that contains Nested_Rules, the CSS_Parser reaches the end of input before the At_Rule_Block's closing brace, or encounters a construct matching neither a valid declaration, a valid style rule, nor a valid nested at-rule, THEN THE CSS_Parser SHALL report a parse error identifying the offending token rather than emit partial or flattened output.

### Requirement 8: Emit correct minified output for nested rules

**User Story:** As a developer, I want nested CSS to be minified correctly, so that the output is compact and semantically equivalent to the input.

#### Acceptance Criteria

1. WHEN the CSS_Parser produces Minified_Output for a Parent_Rule containing Nested_Rules, THE CSS_Parser SHALL emit each Nested_Rule inside the braces of the Parent_Rule, with no whitespace between the Parent_Rule's opening brace and the first enclosed item and no whitespace between a Nested_Rule's closing brace and the item that follows it.
2. WHEN the CSS_Parser produces Minified_Output for a Parent_Rule or a Nested_Rule, THE CSS_Parser SHALL omit all whitespace immediately inside and outside braces, around the colon that separates a declaration's property from its value, and around commas in selector lists.
3. WHEN the CSS_Parser produces Minified_Output for a Nested_Selector, THE CSS_Parser SHALL remove whitespace around the child (`>`), next-sibling (`+`), and subsequent-sibling (`~`) Combinators while retaining exactly one space for the descendant Combinator.
4. WHERE the `RemoveEmptyBlocks` setting is enabled, WHEN a Nested_Rule's declarations are all removed so that its Declaration_Block becomes empty, THE CSS_Parser SHALL omit that Nested_Rule from the output.
5. WHERE the `RemoveEmptyBlocks` setting is enabled, WHEN removing empty Nested_Rules and declarations leaves a Parent_Rule with an empty Declaration_Block, THE CSS_Parser SHALL omit that Parent_Rule from the output.
6. WHEN the CSS_Parser produces Minified_Output for a Nested_Rule that is immediately followed by a declaration within the same Declaration_Block, THE CSS_Parser SHALL emit the following declaration directly after the Nested_Rule's closing brace without inserting any separator character.
7. WHEN the CSS_Parser produces Pretty_Output for a Parent_Rule containing Nested_Rules, THE CSS_Parser SHALL indent each Nested_Rule one indentation level deeper than its Parent_Rule's declarations, using the same indentation unit it applies to non-nested rules.

### Requirement 9: Preserve existing non-nested behavior

**User Story:** As a developer, I want existing CSS that does not use nesting to be unaffected, so that adding nesting support does not introduce regressions.

#### Acceptance Criteria

1. WHEN the CSS_Parser processes CSS that contains no Nesting_Selector and no Nested_Rules, THE CSS_Parser SHALL produce output that is byte-for-byte identical to the output the CSS_Parser produced for the same input under the same CssSettings before this feature was added, in both Minified_Output and Pretty_Output modes.
2. WHEN the CSS_Parser processes a Declaration_Block that contains only declarations and no Nested_Rules, THE CSS_Parser SHALL parse the block using the existing declaration parsing behavior and produce output that is byte-for-byte identical to the output produced for the same Declaration_Block under the same CssSettings before this feature was added.
3. IF the CSS_Parser processes CSS that contains no Nesting_Selector and no Nested_Rules and that CSS contains a syntax error, THEN THE CSS_Parser SHALL report the same set of parse errors, each identifying the same offending token and source position, that it reported for the same input before this feature was added.
