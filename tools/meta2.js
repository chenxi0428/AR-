JSON.stringify({
  headerInfo: [...document.querySelectorAll('*')]
    .filter((e) => e.children.length === 0 && /最新修改时间|创建时间/.test(e.textContent || ''))
    .map((e) => (e.textContent || '').trim())
    .slice(0, 5),
  bodyHasModified: (document.body.innerText || '').includes('最新修改时间'),
  modifiedMatch: ((document.body.innerText || '').match(/最新修改时间为[^\n]{0,20}/) || [])[0] || null,
})
