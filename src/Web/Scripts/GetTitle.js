(function(){
	// Get Document Titles & Sections
	let titles = Array.from(document.querySelectorAll('.breadcrumb-link')).filter(el => el.title && el.title.trim() !== '');
	titles.shift();
	let title = titles.map(el => `<span data-link="true" href="${el.href}">${el.title.trim()}</span>`).join(' ⮞ ');
	const active = document.querySelector('.breadcrumb-item.active').textContent.trim();
	const ogUrl = document.querySelector('meta[property="og:url"]')?.content?.split("#")[0] || "";
	title += (titles.length ? ' ⮞ ' : '') + (active ? `<span data-link="true" href="#">${active}</span>` : '');
	const sections = document.querySelectorAll('h1[id], h2[id], h3[id], h4[id], h5[id], h6[id]');
	const sectionlist = document.querySelectorAll('.list-item .list-link');
	let data = null;
	getTitle = (anim = true) => {
		let name = null;
		let frag = null;
		sections.forEach(sec => {
			const rect = sec.getBoundingClientRect();
			if (rect.top <= 100) frag = sec.id
			if (frag === sec.id) name = sec.textContent.trim();
		});
		name = (title ? title : '') + (name ? ' # ' + `<span data-link="true" href="${ogUrl}#${frag}">${name}</span>` : '');
		window.chrome.webview.postMessage({
			type: 'UpdateTitle'
		});
		if (data === name) return;
		window.chrome.webview.postMessage({
			type: 'SetTitle',
			anim: anim,
			data: data = name
		});
		sectionlist.forEach(item => {
			const href = item.getAttribute('href');
			const url = new URL(href);
			const match = url.hash === '#' + frag;
			if (match) {
				item.classList.add('is-active');
			} else {
				item.classList.remove('is-active');
			}
		});
	}
})();