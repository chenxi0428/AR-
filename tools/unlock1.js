(() => {
  const pwd = 'UNITY2026';
  const input = document.querySelector('input.password-input') || document.querySelector('input[type="password"]');
  if (!input) return 'NO_INPUT';
  input.focus();
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
  setter.call(input, pwd);
  input.dispatchEvent(new Event('input', { bubbles: true }));
  input.dispatchEvent(new Event('change', { bubbles: true }));
  input.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: 'Enter', code: 'Enter' }));
  input.dispatchEvent(new KeyboardEvent('keyup', { bubbles: true, key: 'Enter', code: 'Enter' }));
  const btn = [...document.querySelectorAll('button')].find((b) => (b.innerText || '').trim() === '确定');
  if (btn) btn.click();
  return JSON.stringify({ typed: input.value, clicked: !!btn });
})()
