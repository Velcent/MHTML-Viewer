(function () {
	if (window.__mhtmlEpicCodeReady) return;
	window.__mhtmlEpicCodeReady = true;

	const collapsedLines = 14;
	const expandedText = "Collapse code";
	const collapsedText = "Expand code";
	const copyText = "Copy full snippet";
	const copiedText = "Copied";

	const keywords = new Set([
		"alignas", "alignof", "and", "and_eq", "as", "asm", "async", "auto", "await", "bool", "break",
		"case", "catch", "char", "class", "concept", "const", "constexpr", "const_cast", "continue",
		"decltype", "default", "def", "delete", "do", "double", "dynamic_cast", "elif", "else", "enum",
		"except", "explicit", "export", "extends", "extern", "false", "final", "finally", "float", "for",
		"friend", "from", "if", "import", "in", "inline", "int", "interface", "long", "mutable", "namespace",
		"new", "noexcept", "not", "nullptr", "operator", "or", "override", "private", "protected", "public",
		"raise", "register", "reinterpret_cast", "requires", "return", "short", "signed", "sizeof", "static",
		"static_cast", "struct", "super", "switch", "template", "this", "throw", "true", "try", "typedef",
		"typeid", "typename", "union", "unsigned", "using", "var", "virtual", "void", "volatile", "while",
		"with", "yield"
	]);
	const literals = new Set(["None", "True", "False", "null", "undefined", "nullptr", "true", "false"]);
	const builtins = new Set([
		"FString", "FName", "FText", "FVector", "FVector2D", "FRotator", "FTransform", "TArray", "TMap",
		"TSet", "TSubclassOf", "UCLASS", "USTRUCT", "UENUM", "UPROPERTY", "UFUNCTION", "GENERATED_BODY",
		"Super", "Cast", "CastChecked", "AddMovementInput", "GetActorRightVector", "GetActorForwardVector"
	]);

	injectStyle();
	setupCodeSnippets();
	setupPlainSnippets();

	function injectStyle() {
		if (document.getElementById("mhtml-epic-code-style")) return;
		const style = document.createElement("style");
		style.id = "mhtml-epic-code-style";
		style.textContent = `
			block-code-snippet.mhtml-code-ready {
				display: block;
				margin: 1rem 0 1.35rem;
				border: 1px solid rgba(255, 255, 255, .1);
				border-radius: 10px;
				overflow: hidden;
				background: #17181d;
				box-shadow: 0 12px 28px rgba(0, 0, 0, .22);
			}
			block-code-snippet.mhtml-code-ready .block-code-snippet-header {
				display: flex;
				align-items: center;
				gap: .75rem;
				min-height: 40px;
				padding: .65rem .9rem;
				border-bottom: 1px solid rgba(255, 255, 255, .08);
				background: linear-gradient(180deg, #24262d, #1c1d23);
				color: #d7dce7;
			}
			block-code-snippet.mhtml-code-ready .block-code-snippet-header-type {
				color: #7bdcff;
				font-size: .78rem;
				font-weight: 700;
				letter-spacing: .04em;
				text-transform: uppercase;
			}
			block-code-snippet.mhtml-code-ready .block-code-snippet-header-title {
				color: #f0f2f7;
				font-size: .9rem;
				font-weight: 600;
			}
			block-code-snippet.mhtml-code-ready .pre-block {
				position: relative;
				background: #111217;
			}
			block-code-snippet.mhtml-code-ready pre {
				margin: 0;
				padding: 1rem 1.15rem;
				overflow: auto;
				color: #d7dce7;
				background: transparent;
				tab-size: 4;
				white-space: pre;
				font: 13px/1.58 "Cascadia Code", "Consolas", monospace;
			}
			block-code-snippet.mhtml-code-ready code {
				color: inherit;
				background: transparent;
				font: inherit;
			}
			block-code-snippet.mhtml-code-ready.is-collapsed .pre-block {
				max-height: calc(${collapsedLines} * 1.58em + 2rem);
				overflow: hidden;
			}
			block-code-snippet.mhtml-code-ready .pre-overlay {
				display: none;
				position: absolute;
				inset: auto 0 0;
				height: 76px;
				pointer-events: none;
				background: linear-gradient(180deg, rgba(17, 18, 23, 0), #111217 82%);
			}
			block-code-snippet.mhtml-code-ready.is-collapsed .pre-overlay {
				display: block;
			}
			block-code-snippet.mhtml-code-ready .block-code-snippet-actions {
				display: flex;
				flex-wrap: wrap;
				gap: .5rem;
				padding: .7rem .9rem;
				border-top: 1px solid rgba(255, 255, 255, .08);
				background: #1b1c22;
			}
			block-code-snippet.mhtml-code-ready .block-code-snippet-actions button {
				border: 1px solid rgba(255, 255, 255, .12);
				border-radius: 7px;
				padding: .42rem .7rem;
				background: #2a2c34;
				color: #f2f5fb;
				cursor: pointer;
				font: 600 12px/1.2 inherit;
			}
			block-code-snippet.mhtml-code-ready .block-code-snippet-actions button:hover {
				border-color: rgba(123, 220, 255, .45);
				background: #343743;
			}
			block-code-snippet.mhtml-code-ready .block-code-snippet-actions button[hidden] {
				display: none;
			}
			.mhtml-code-keyword { color: #ff7ab6; }
			.mhtml-code-string { color: #f6d365; }
			.mhtml-code-comment { color: #778195; font-style: italic; }
			.mhtml-code-number { color: #b5a7ff; }
			.mhtml-code-literal { color: #7bdcff; }
			.mhtml-code-builtin { color: #8ee6a7; }
			.mhtml-code-symbol { color: #f0a36b; }
		`;
		document.head.appendChild(style);
	}

	function setupCodeSnippets() {
		for (const snippet of document.querySelectorAll("block-code-snippet")) {
			setupSnippet(snippet);
		}
	}

	function setupPlainSnippets() {
		for (const pre of document.querySelectorAll("pre.block-code-snippet-plain")) {
			if (pre.closest("block-code-snippet")) continue;
			const snippet = document.createElement("block-code-snippet");
			snippet.className = "block-code-snippet mhtml-code-snippet";

			const header = document.createElement("header");
			header.className = "block-code-snippet-header";
			const type = document.createElement("div");
			type.className = "block-code-snippet-header-type";
			type.textContent = languageLabel(readLanguage(pre.querySelector("code") || pre));
			header.appendChild(type);

			const preBlock = document.createElement("div");
			preBlock.className = "pre-block";
			const overlay = document.createElement("div");
			overlay.className = "pre-overlay";

			pre.parentNode.insertBefore(snippet, pre);
			preBlock.appendChild(pre);
			preBlock.appendChild(overlay);
			snippet.appendChild(header);
			snippet.appendChild(preBlock);
			snippet.appendChild(createActions(true));
			setupSnippet(snippet);
		}
	}

	function setupSnippet(snippet) {
		if (snippet.dataset.mhtmlCodeReady === "true") return;
		const code = snippet.querySelector("code");
		const preBlock = snippet.querySelector(".pre-block");
		if (!code || !preBlock) return;

		snippet.dataset.mhtmlCodeReady = "true";
		snippet.classList.add("mhtml-code-ready");

		if (!preBlock.querySelector(".pre-overlay")) {
			const overlay = document.createElement("div");
			overlay.className = "pre-overlay";
			preBlock.appendChild(overlay);
		}

		highlightCode(code, readLanguage(code, snippet));

		let actions = snippet.querySelector(".block-code-snippet-actions");
		if (!actions) {
			actions = createActions(true);
			snippet.appendChild(actions);
		}

		const lineCount = countLines(code.textContent || "");
		const canExpand = lineCount > collapsedLines;
		wireButtons(snippet, actions, canExpand);
		setExpanded(snippet, !canExpand);
	}

	function createActions(includeExpand) {
		const actions = document.createElement("div");
		actions.className = "block-code-snippet-actions";
		if (includeExpand) {
			const expand = document.createElement("button");
			expand.type = "button";
			expand.dataset.mhtmlCodeAction = "expand";
			expand.textContent = collapsedText;
			actions.appendChild(expand);
		}
		const copy = document.createElement("button");
		copy.type = "button";
		copy.dataset.mhtmlCodeAction = "copy";
		copy.textContent = copyText;
		actions.appendChild(copy);
		return actions;
	}

	function wireButtons(snippet, actions, canExpand) {
		const buttons = Array.from(actions.querySelectorAll("button"));
		let expand = buttons.find(button => button.dataset.mhtmlCodeAction === "expand" || /expand|collapse|show/i.test(button.textContent || ""));
		let copy = buttons.find(button => button.dataset.mhtmlCodeAction === "copy" || /copy/i.test(button.textContent || ""));

		if (!expand && canExpand) {
			expand = document.createElement("button");
			expand.type = "button";
			expand.dataset.mhtmlCodeAction = "expand";
			actions.insertBefore(expand, actions.firstChild);
		}
		if (!copy) {
			copy = document.createElement("button");
			copy.type = "button";
			copy.dataset.mhtmlCodeAction = "copy";
			actions.appendChild(copy);
		}

		if (expand) {
			expand.hidden = !canExpand;
			expand.dataset.mhtmlCodeAction = "expand";
			expand.addEventListener("click", event => {
				event.preventDefault();
				setExpanded(snippet, !snippet.classList.contains("is-expanded"));
			});
		}

		copy.dataset.mhtmlCodeAction = "copy";
		copy.addEventListener("click", async event => {
			event.preventDefault();
			const previous = copy.textContent;
			await copyToClipboard(snippet.querySelector("code")?.textContent || "");
			copy.textContent = copiedText;
			setTimeout(() => {
				copy.textContent = previous && /copy/i.test(previous) ? previous : copyText;
			}, 1200);
		});
	}

	function setExpanded(snippet, expanded) {
		snippet.classList.toggle("is-expanded", expanded);
		snippet.classList.toggle("is-collapsed", !expanded);
		const expand = snippet.querySelector("[data-mhtml-code-action='expand']");
		if (expand) expand.textContent = expanded ? expandedText : collapsedText;
	}

	function highlightCode(code, language) {
		if (code.dataset.mhtmlHighlighted === "true") return;
		code.dataset.mhtmlHighlighted = "true";
		code.innerHTML = colorize(code.textContent || "", language);
	}

	function colorize(text, language) {
		let output = "";
		let i = 0;
		while (i < text.length) {
			const ch = text[i];
			const next = text[i + 1] || "";

			if (ch === "/" && next === "/") {
				const end = findLineEnd(text, i);
				output += span("comment", text.slice(i, end));
				i = end;
				continue;
			}
			if (ch === "/" && next === "*") {
				const end = text.indexOf("*/", i + 2);
				const stop = end === -1 ? text.length : end + 2;
				output += span("comment", text.slice(i, stop));
				i = stop;
				continue;
			}
			if (ch === "#" && shouldTreatHashAsComment(text, i, language)) {
				const end = findLineEnd(text, i);
				output += span("comment", text.slice(i, end));
				i = end;
				continue;
			}
			if (ch === '"' || ch === "'" || ch === "`") {
				const end = findStringEnd(text, i, ch);
				output += span("string", text.slice(i, end));
				i = end;
				continue;
			}
			if (isDigit(ch)) {
				const end = scanNumber(text, i);
				output += span("number", text.slice(i, end));
				i = end;
				continue;
			}
			if (isWordStart(ch)) {
				const end = scanWord(text, i);
				const word = text.slice(i, end);
				if (builtins.has(word)) output += span("builtin", word);
				else if (literals.has(word)) output += span("literal", word);
				else if (keywords.has(word)) output += span("keyword", word);
				else output += escapeHtml(word);
				i = end;
				continue;
			}
			if (ch === "@" || ch === "&") {
				const end = scanSymbol(text, i + 1);
				if (end > i + 1) {
					output += span("symbol", text.slice(i, end));
					i = end;
					continue;
				}
			}

			output += escapeHtml(ch);
			i++;
		}
		return output;
	}

	function shouldTreatHashAsComment(text, index, language) {
		const lang = (language || "").toLowerCase();
		if (lang.includes("python") || lang.includes("bash") || lang.includes("shell") || lang.includes("ini")) return true;
		let cursor = index - 1;
		while (cursor >= 0 && text[cursor] !== "\n" && text[cursor] !== "\r") {
			if (!/\s/.test(text[cursor])) return false;
			cursor--;
		}
		return true;
	}

	function readLanguage(element, snippet) {
		const values = [
			element?.className || "",
			snippet?.querySelector(".block-code-snippet-header-type")?.textContent || ""
		].join(" ").toLowerCase();
		const match = values.match(/language-([a-z0-9+#-]+)/);
		if (match) return match[1];
		if (values.includes("c++") || values.includes("cpp")) return "cpp";
		if (values.includes("c#") || values.includes("csharp")) return "csharp";
		if (values.includes("python")) return "python";
		if (values.includes("json")) return "json";
		if (values.includes("xml") || values.includes("html")) return "html";
		return "";
	}

	function languageLabel(language) {
		const value = (language || "").toLowerCase();
		if (value === "cpp" || value === "c++") return "C++";
		if (value === "csharp" || value === "cs" || value === "c#") return "C#";
		if (value === "js" || value === "javascript") return "JavaScript";
		if (value === "py" || value === "python") return "Python";
		return value ? value.toUpperCase() : "Code";
	}

	async function copyToClipboard(text) {
		if (navigator.clipboard && window.isSecureContext) {
			await navigator.clipboard.writeText(text);
			return;
		}
		const input = document.createElement("textarea");
		input.value = text;
		input.style.position = "fixed";
		input.style.left = "-9999px";
		document.body.appendChild(input);
		input.focus();
		input.select();
		document.execCommand("copy");
		input.remove();
	}

	function countLines(text) {
		if (!text) return 0;
		return text.split(/\r\n|\r|\n/).length;
	}

	function findLineEnd(text, start) {
		const index = text.indexOf("\n", start);
		return index === -1 ? text.length : index;
	}

	function findStringEnd(text, start, quote) {
		let i = start + 1;
		while (i < text.length) {
			if (text[i] === "\\") {
				i += 2;
				continue;
			}
			if (text[i] === quote) return i + 1;
			i++;
		}
		return text.length;
	}

	function scanNumber(text, start) {
		let i = start;
		while (i < text.length && /[0-9a-fA-FxX._]/.test(text[i])) i++;
		return i;
	}

	function scanWord(text, start) {
		let i = start + 1;
		while (i < text.length && isWord(text[i])) i++;
		return i;
	}

	function scanSymbol(text, start) {
		let i = start;
		while (i < text.length && isWord(text[i])) i++;
		return i;
	}

	function isDigit(value) {
		return value >= "0" && value <= "9";
	}

	function isWordStart(value) {
		return /[A-Za-z_]/.test(value);
	}

	function isWord(value) {
		return /[A-Za-z0-9_]/.test(value);
	}

	function span(kind, value) {
		return `<span class="mhtml-code-${kind}">${escapeHtml(value)}</span>`;
	}

	function escapeHtml(value) {
		return value
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;");
	}
})();
