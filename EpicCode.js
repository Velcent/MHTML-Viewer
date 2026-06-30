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
				margin: 1.25rem 0 1.5rem;
				padding: 20px 22px 22px;
				border: 1px solid #3b3c42;
				border-radius: 16px;
				overflow: hidden;
				background: #121318;
				box-shadow: none;
			}
			block-code-snippet.mhtml-code-ready .block-code-snippet-header {
				display: flex;
				align-items: center;
				gap: 8px;
				min-height: 0;
				padding: 0 0 38px;
				border: 0;
				background: transparent;
				color: #f4f4f5;
			}
			block-code-snippet.mhtml-code-ready .block-code-snippet-header-type {
				display: inline-flex;
				align-items: center;
				min-height: 20px;
				padding: 2px 8px;
				border-radius: 4px;
				background: #303139;
				color: #f5f5f6;
				font-size: 12px;
				font-weight: 700;
				letter-spacing: 0;
				line-height: 1.25;
				text-transform: none;
			}
			block-code-snippet.mhtml-code-ready .block-code-snippet-header-title {
				color: #cfd3dc;
				font-size: 13px;
				font-weight: 600;
			}
			block-code-snippet.mhtml-code-ready .pre-block {
				position: relative;
				margin: 0;
				border: 0;
				border-radius: 0;
				overflow: hidden;
				background: #141519;
			}
			block-code-snippet.mhtml-code-ready pre {
				margin: 0;
				padding: 0;
				overflow: auto;
				color: #f4f4f5;
				background: transparent;
				tab-size: 4;
				white-space: pre;
				font: 14px/1.7 "Cascadia Code", "Consolas", "Courier New", monospace;
			}
			block-code-snippet.mhtml-code-ready code {
				color: inherit;
				background: transparent;
				font: inherit;
			}
			block-code-snippet.mhtml-code-ready.is-collapsed .pre-block {
				max-height: calc(${collapsedLines} * 1.7em + 32px);
				overflow: hidden;
			}
			block-code-snippet.mhtml-code-ready .pre-overlay {
				display: none;
				position: absolute;
				inset: auto 0 0;
				height: 82px;
				pointer-events: none;
				background: linear-gradient(180deg, rgba(20, 21, 25, 0), #141519 86%);
			}
			block-code-snippet.mhtml-code-ready.is-collapsed .pre-overlay {
				display: block;
			}
			block-code-snippet.mhtml-code-ready .block-code-snippet-actions {
				display: flex;
				flex-wrap: wrap;
				gap: 8px;
				padding: 26px 0 0;
				border: 0;
				background: transparent;
			}
			block-code-snippet.mhtml-code-ready .block-code-snippet-actions button {
				display: inline-flex;
				align-items: center;
				gap: 8px;
				border: 0;
				border-radius: 4px;
				padding: 5px 10px;
				background: #3a3b43;
				color: #f4f4f5;
				cursor: pointer;
				font: 700 12px/1.25 inherit;
			}
			block-code-snippet.mhtml-code-ready .block-code-snippet-actions button:hover {
				background: #494b55;
			}
			block-code-snippet.mhtml-code-ready .block-code-snippet-actions button[hidden] {
				display: none;
			}
			block-code-snippet.mhtml-code-ready .mhtml-code-lines {
				display: table;
				width: 100%;
				border-collapse: collapse;
				padding: 0;
			}
			block-code-snippet.mhtml-code-ready .mhtml-code-line {
				display: table-row;
			}
			block-code-snippet.mhtml-code-ready .mhtml-code-line-number {
				display: table-cell;
				min-width: 28px;
				padding: 0 12px 0 10px;
				color: #d7ecff;
				text-align: right;
				user-select: none;
				white-space: pre;
			}
			block-code-snippet.mhtml-code-ready .mhtml-code-line-content {
				display: table-cell;
				width: 100%;
				padding-right: 20px;
				white-space: pre;
			}
			block-code-snippet.mhtml-code-ready .mhtml-code-line:first-child .mhtml-code-line-number,
			block-code-snippet.mhtml-code-ready .mhtml-code-line:first-child .mhtml-code-line-content {
				padding-top: 18px;
			}
			block-code-snippet.mhtml-code-ready .mhtml-code-line:last-child .mhtml-code-line-number,
			block-code-snippet.mhtml-code-ready .mhtml-code-line:last-child .mhtml-code-line-content {
				padding-bottom: 18px;
			}
			.mhtml-code-keyword { color: #f052ff; }
			.mhtml-code-string { color: #9be871; }
			.mhtml-code-comment { color: #697487; font-style: italic; }
			.mhtml-code-number { color: #b8a2ff; }
			.mhtml-code-literal { color: #71c8ff; }
			.mhtml-code-builtin { color: #61d6ff; }
			.mhtml-code-symbol { color: #3aa0ff; }
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

		const lineCount = countLines(code.dataset.mhtmlRawCode || code.textContent || "");
		const canExpand = shouldShowExpand(actions, lineCount);
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
			setButtonLabel(expand, collapsedText);
			actions.appendChild(expand);
		}
		const copy = document.createElement("button");
		copy.type = "button";
		copy.dataset.mhtmlCodeAction = "copy";
		setButtonLabel(copy, copyText);
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
			expand.hidden = false;
			expand.dataset.mhtmlCodeAction = "expand";
			expand.addEventListener("click", event => {
				event.preventDefault();
				setExpanded(snippet, !snippet.classList.contains("is-expanded"));
			});
		}

		copy.dataset.mhtmlCodeAction = "copy";
		copy.dataset.mhtmlCodeSuffix = copy.querySelector(".ps-1")?.textContent || copy.dataset.mhtmlCodeSuffix || "";
		copy.addEventListener("click", async event => {
			event.preventDefault();
			const previous = readButtonLabel(copy) || copyText;
			const code = snippet.querySelector("code");
			await copyToClipboard(code?.dataset.mhtmlRawCode || code?.textContent || "");
			setButtonLabel(copy, copiedText);
			setTimeout(() => {
				setButtonLabel(copy, /copy/i.test(previous) ? previous : copyText);
			}, 1200);
		});
	}

	function shouldShowExpand(actions, lineCount) {
		if (lineCount > collapsedLines) return true;
		const text = actions.textContent || "";
		if (/expand|collapse|show/i.test(text)) return true;
		const match = text.match(/\((\d+)\s+lines?\s+long\)/i);
		return match ? Number(match[1]) > collapsedLines : false;
	}

	function setExpanded(snippet, expanded) {
		snippet.classList.toggle("is-expanded", expanded);
		snippet.classList.toggle("is-collapsed", !expanded);
		const expand = snippet.querySelector("[data-mhtml-code-action='expand']");
		if (expand) setButtonLabel(expand, expanded ? expandedText : collapsedText);
	}

	function setButtonLabel(button, label) {
		const icon = button.querySelector(".eds-icon")?.cloneNode(true);
		const count = button.dataset.mhtmlCodeSuffix || button.querySelector(".ps-1")?.textContent || "";
		button.textContent = "";
		if (icon) button.appendChild(icon);
		button.appendChild(document.createTextNode(label));
		if (count && /copy/i.test(label)) {
			const suffix = document.createElement("span");
			suffix.className = "ps-1";
			suffix.textContent = count;
			button.appendChild(suffix);
		}
	}

	function readButtonLabel(button) {
		return Array.from(button.childNodes)
			.filter(node => node.nodeType === Node.TEXT_NODE)
			.map(node => node.textContent || "")
			.join("")
			.trim();
	}

	function highlightCode(code, language) {
		if (code.dataset.mhtmlHighlighted === "true") return;
		code.dataset.mhtmlHighlighted = "true";
		const raw = code.textContent || "";
		code.dataset.mhtmlRawCode = raw;
		code.innerHTML = renderLineNumbers(raw, language);
	}

	function renderLineNumbers(text, language) {
		const lines = text.split(/\r\n|\r|\n/);
		return `<span class="mhtml-code-lines">${lines.map((line, index) => {
			const content = line.length ? colorize(line, language) : " ";
			return `<span class="mhtml-code-line"><span class="mhtml-code-line-number">${index + 1}</span><span class="mhtml-code-line-content">${content}</span></span>`;
		}).join("")}</span>`;
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
		if (lang.includes("cpp") || lang.includes("c++") || lang === "c" || lang.includes("csharp")) return false;
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
