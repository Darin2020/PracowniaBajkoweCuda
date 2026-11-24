import { expectAccessible } from './axe';

describe('axe sanity', () => {
  it('catches an unlabelled input', async () => {
    const div = document.createElement('div');
    div.innerHTML = '<input type="text"><img src="x.png"><button></button>';
    document.body.appendChild(div);
    await expect(expectAccessible(div)).rejects.toThrow();
  });
});
