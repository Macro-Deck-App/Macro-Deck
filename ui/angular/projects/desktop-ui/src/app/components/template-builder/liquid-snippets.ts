import { AppStrings } from '@macro-deck/runtime';

export type FilterCategory = 'text' | 'numbers' | 'dates' | 'lists' | 'fallbacks' | 'scriban';

export interface LiquidFilterSnippet {
  key: string;
  category: FilterCategory;
  label: string;
  insert: string;
  hintKey: string;
}

export interface LiquidControlSnippet {
  key: string;
  label: string;
  example: string;
  hintKey: string;
  insert: string;
  caret: number;
}

const FILTER_CATEGORY_KEYS: Record<FilterCategory, string> = {
  text: AppStrings.TemplateBuilder.FilterCategory.Text,
  numbers: AppStrings.TemplateBuilder.FilterCategory.Numbers,
  dates: AppStrings.TemplateBuilder.FilterCategory.Dates,
  lists: AppStrings.TemplateBuilder.FilterCategory.Lists,
  fallbacks: AppStrings.TemplateBuilder.FilterCategory.Fallbacks,
  scriban: AppStrings.TemplateBuilder.FilterCategory.Scriban,
};

export function filterCategoryLabelKey(category: FilterCategory): string {
  return FILTER_CATEGORY_KEYS[category];
}

export const FILTER_CATEGORIES: FilterCategory[] = ['text', 'numbers', 'dates', 'lists', 'fallbacks', 'scriban'];

export const LIQUID_FILTERS: LiquidFilterSnippet[] = [
  // Text
  { key: 'upcase', category: 'text', label: 'upcase', insert: ' | upcase', hintKey: AppStrings.TemplateBuilder.Filters.Upcase.Hint },
  { key: 'downcase', category: 'text', label: 'downcase', insert: ' | downcase', hintKey: AppStrings.TemplateBuilder.Filters.Downcase.Hint },
  { key: 'capitalize', category: 'text', label: 'capitalize', insert: ' | capitalize', hintKey: AppStrings.TemplateBuilder.Filters.Capitalize.Hint },
  { key: 'plain_text', category: 'text', label: 'plain_text', insert: ' | plain_text', hintKey: AppStrings.TemplateBuilder.Filters.PlainText.Hint },
  { key: 'strip', category: 'text', label: 'strip', insert: ' | strip', hintKey: AppStrings.TemplateBuilder.Filters.Strip.Hint },
  { key: 'lstrip', category: 'text', label: 'lstrip', insert: ' | lstrip', hintKey: AppStrings.TemplateBuilder.Filters.Lstrip.Hint },
  { key: 'rstrip', category: 'text', label: 'rstrip', insert: ' | rstrip', hintKey: AppStrings.TemplateBuilder.Filters.Rstrip.Hint },
  { key: 'truncate', category: 'text', label: 'truncate', insert: ' | truncate: 20', hintKey: AppStrings.TemplateBuilder.Filters.Truncate.Hint },
  { key: 'truncatewords', category: 'text', label: 'truncatewords', insert: ' | truncatewords: 3', hintKey: AppStrings.TemplateBuilder.Filters.Truncatewords.Hint },
  { key: 'append', category: 'text', label: 'append', insert: ' | append: "!"', hintKey: AppStrings.TemplateBuilder.Filters.Append.Hint },
  { key: 'prepend', category: 'text', label: 'prepend', insert: ' | prepend: ">> "', hintKey: AppStrings.TemplateBuilder.Filters.Prepend.Hint },
  { key: 'remove_first', category: 'text', label: 'remove_first', insert: ' | remove_first: "x"', hintKey: AppStrings.TemplateBuilder.Filters.RemoveFirst.Hint },
  { key: 'strip_html', category: 'text', label: 'strip_html', insert: ' | strip_html', hintKey: AppStrings.TemplateBuilder.Filters.StripHtml.Hint },
  { key: 'strip_newlines', category: 'text', label: 'strip_newlines', insert: ' | strip_newlines', hintKey: AppStrings.TemplateBuilder.Filters.StripNewlines.Hint },
  { key: 'escape', category: 'text', label: 'escape', insert: ' | escape', hintKey: AppStrings.TemplateBuilder.Filters.Escape.Hint },

  // Numbers
  { key: 'round', category: 'numbers', label: 'round', insert: ' | round: 2', hintKey: AppStrings.TemplateBuilder.Filters.Round.Hint },
  { key: 'plus', category: 'numbers', label: 'plus', insert: ' | plus: 1', hintKey: AppStrings.TemplateBuilder.Filters.Plus.Hint },
  { key: 'minus', category: 'numbers', label: 'minus', insert: ' | minus: 1', hintKey: AppStrings.TemplateBuilder.Filters.Minus.Hint },
  { key: 'times', category: 'numbers', label: 'times', insert: ' | times: 2', hintKey: AppStrings.TemplateBuilder.Filters.Times.Hint },
  { key: 'divided_by', category: 'numbers', label: 'divided_by', insert: ' | divided_by: 2', hintKey: AppStrings.TemplateBuilder.Filters.DividedBy.Hint },
  { key: 'floor', category: 'numbers', label: 'floor', insert: ' | floor', hintKey: AppStrings.TemplateBuilder.Filters.Floor.Hint },
  { key: 'ceil', category: 'numbers', label: 'ceil', insert: ' | ceil', hintKey: AppStrings.TemplateBuilder.Filters.Ceil.Hint },
  { key: 'modulo', category: 'numbers', label: 'modulo', insert: ' | modulo: 3', hintKey: AppStrings.TemplateBuilder.Filters.Modulo.Hint },
  { key: 'abs', category: 'numbers', label: 'abs', insert: ' | abs', hintKey: AppStrings.TemplateBuilder.Filters.Abs.Hint },

  // Dates
  { key: 'date', category: 'dates', label: 'date', insert: ' | date: "%Y-%m-%d"', hintKey: AppStrings.TemplateBuilder.Filters.Date.Hint },

  // Lists (Liquid `split` turns a string into an array, so these read naturally after it)
  { key: 'split', category: 'lists', label: 'split', insert: ' | split: ","', hintKey: AppStrings.TemplateBuilder.Filters.Split.Hint },
  { key: 'join', category: 'lists', label: 'join', insert: ' | join: ", "', hintKey: AppStrings.TemplateBuilder.Filters.Join.Hint },
  { key: 'first', category: 'lists', label: 'first', insert: ' | first', hintKey: AppStrings.TemplateBuilder.Filters.First.Hint },
  { key: 'last', category: 'lists', label: 'last', insert: ' | last', hintKey: AppStrings.TemplateBuilder.Filters.Last.Hint },
  { key: 'size', category: 'lists', label: 'size', insert: ' | size', hintKey: AppStrings.TemplateBuilder.Filters.Size.Hint },
  { key: 'sort', category: 'lists', label: 'sort', insert: ' | sort', hintKey: AppStrings.TemplateBuilder.Filters.Sort.Hint },
  { key: 'uniq', category: 'lists', label: 'uniq', insert: ' | uniq', hintKey: AppStrings.TemplateBuilder.Filters.Uniq.Hint },
  { key: 'reverse', category: 'lists', label: 'reverse', insert: ' | reverse', hintKey: AppStrings.TemplateBuilder.Filters.Reverse.Hint },
  { key: 'contains', category: 'lists', label: 'contains', insert: ' | contains: "x"', hintKey: AppStrings.TemplateBuilder.Filters.Contains.Hint },

  // Fallbacks
  { key: 'default', category: 'fallbacks', label: 'default', insert: ' | default: "-"', hintKey: AppStrings.TemplateBuilder.Filters.Default.Hint },
  { key: 'replace', category: 'text', label: 'replace', insert: ' | replace: "a", "b"', hintKey: AppStrings.TemplateBuilder.Filters.Replace.Hint },
  { key: 'replace_first', category: 'text', label: 'replace_first', insert: ' | replace_first: "a", "b"', hintKey: AppStrings.TemplateBuilder.Filters.ReplaceFirst.Hint },
  { key: 'remove', category: 'text', label: 'remove', insert: ' | remove: "x"', hintKey: AppStrings.TemplateBuilder.Filters.Remove.Hint },

  // Scriban-native functions (not Liquid, but resolvable through Scriban's dotted module syntax
  // because `LiquidFunctionsToScriban` clones the whole builtin namespace into liquid-compat mode)
  { key: 'string.md5', category: 'scriban', label: 'string.md5', insert: ' | string.md5', hintKey: AppStrings.TemplateBuilder.Filters.StringMd5.Hint },
  { key: 'string.base64_encode', category: 'scriban', label: 'string.base64_encode', insert: ' | string.base64_encode', hintKey: AppStrings.TemplateBuilder.Filters.StringBase64Encode.Hint },
  { key: 'string.pad_left', category: 'scriban', label: 'string.pad_left', insert: ' | string.pad_left 5', hintKey: AppStrings.TemplateBuilder.Filters.StringPadLeft.Hint },
  { key: 'string.index_of', category: 'scriban', label: 'string.index_of', insert: ' | string.index_of "x"', hintKey: AppStrings.TemplateBuilder.Filters.StringIndexOf.Hint },
  { key: 'string.handleize', category: 'scriban', label: 'string.handleize', insert: ' | string.handleize', hintKey: AppStrings.TemplateBuilder.Filters.StringHandleize.Hint },
  { key: 'object.to_json', category: 'scriban', label: 'object.to_json', insert: ' | object.to_json', hintKey: AppStrings.TemplateBuilder.Filters.ObjectToJson.Hint },
  { key: 'html.url_encode', category: 'scriban', label: 'html.url_encode', insert: ' | html.url_encode', hintKey: AppStrings.TemplateBuilder.Filters.HtmlUrlEncode.Hint },
  { key: 'html.newline_to_br', category: 'scriban', label: 'html.newline_to_br', insert: ' | html.newline_to_br', hintKey: AppStrings.TemplateBuilder.Filters.HtmlNewlineToBr.Hint },
  { key: 'math.uuid', category: 'scriban', label: 'math.uuid', insert: ' math.uuid', hintKey: AppStrings.TemplateBuilder.Filters.MathUuid.Hint },
  { key: 'date.now', category: 'scriban', label: 'date.now', insert: ' date.now', hintKey: AppStrings.TemplateBuilder.Filters.DateNow.Hint },
];

function withCaret(template: string): { insert: string; caret: number } {
  const marker = template.indexOf('\0');
  if (marker === -1) return { insert: template, caret: template.length };
  return { insert: template.slice(0, marker) + template.slice(marker + 1), caret: marker };
}

export const LIQUID_CONTROL_SNIPPETS: LiquidControlSnippet[] = [
  { key: 'if-else', label: 'if / else', example: '{% if … %} … {% else %} … {% endif %}',
    hintKey: AppStrings.TemplateBuilder.ControlFlowSnippets.IfElse.Hint,
    ...withCaret('{% if \0 %}\n  \n{% else %}\n  \n{% endif %}') },
  { key: 'if-available', label: 'if available', example: '{% if vars.x.state.is_not_empty %} … {% endif %}',
    hintKey: AppStrings.TemplateBuilder.ControlFlowSnippets.IfAvailable.Hint,
    ...withCaret('{% if vars.\0.state.is_not_empty %}\n  \n{% endif %}') },
  { key: 'unless', label: 'unless', example: '{% unless … %} … {% endunless %}',
    hintKey: AppStrings.TemplateBuilder.ControlFlowSnippets.Unless.Hint,
    ...withCaret('{% unless \0 %}\n  \n{% endunless %}') },
  { key: 'for', label: 'for', example: '{% for item in … %} … {% endfor %}',
    hintKey: AppStrings.TemplateBuilder.ControlFlowSnippets.For.Hint,
    ...withCaret('{% for item in \0 %}\n  {{ item }}\n{% endfor %}') },
  { key: 'case-when', label: 'case / when', example: '{% case … %} {% when … %} … {% endcase %}',
    hintKey: AppStrings.TemplateBuilder.ControlFlowSnippets.CaseWhen.Hint,
    ...withCaret('{% case \0 %}\n{% when "a" %}\n  \n{% else %}\n  \n{% endcase %}') },
  { key: 'assign', label: 'assign', example: '{% assign name = … %}',
    hintKey: AppStrings.TemplateBuilder.ControlFlowSnippets.Assign.Hint,
    ...withCaret('{% assign name = \0 %}') },
  { key: 'capture', label: 'capture', example: '{% capture name %} … {% endcapture %}',
    hintKey: AppStrings.TemplateBuilder.ControlFlowSnippets.Capture.Hint,
    ...withCaret('{% capture name %}\n  \0\n{% endcapture %}') },
  { key: 'comment', label: 'comment', example: '{% comment %} … {% endcomment %}',
    hintKey: AppStrings.TemplateBuilder.ControlFlowSnippets.Comment.Hint,
    ...withCaret('{% comment %}\n  \0\n{% endcomment %}') },
  { key: 'break', label: 'break', example: '{% for … %} {% if … %} {% break %} … {% endfor %}',
    hintKey: AppStrings.TemplateBuilder.ControlFlowSnippets.Break.Hint,
    ...withCaret('{% for item in \0 %}\n  {% if item == "" %}{% break %}{% endif %}\n  {{ item }}\n{% endfor %}') },
  { key: 'continue', label: 'continue', example: '{% for … %} {% if … %} {% continue %} … {% endfor %}',
    hintKey: AppStrings.TemplateBuilder.ControlFlowSnippets.Continue.Hint,
    ...withCaret('{% for item in \0 %}\n  {% if item == "" %}{% continue %}{% endif %}\n  {{ item }}\n{% endfor %}') },
  { key: 'raw', label: 'raw / endraw', example: '{% raw %} … {% endraw %}',
    hintKey: AppStrings.TemplateBuilder.ControlFlowSnippets.Raw.Hint,
    ...withCaret('{% raw %}\n  \0\n{% endraw %}') },
  { key: 'ifchanged', label: 'ifchanged', example: '{% for … %} {% ifchanged %} … {% endifchanged %} {% endfor %}',
    hintKey: AppStrings.TemplateBuilder.ControlFlowSnippets.Ifchanged.Hint,
    ...withCaret('{% for item in \0 %}\n  {% ifchanged %}{{ item }}{% endifchanged %}\n{% endfor %}') },
];
