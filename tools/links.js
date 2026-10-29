JSON.stringify({
  links: [...document.querySelectorAll('a[href*="/wiki/"]')]
    .map((a) => ({ t: (a.innerText || '').trim().slice(0, 60), h: a.getAttribute('href') }))
    .filter((x) => x.t)
    .slice(0, 40),
  breadcrumb: [...document.querySelectorAll('[class*="breadcrumb"]')].map((e) => (e.innerText || '').trim()).slice(0, 5),
  spaceTitle: (document.querySelector('.space-title, [class*="space-name"]')?.innerText || '').trim(),
})
