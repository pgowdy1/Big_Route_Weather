import { TestBed } from '@angular/core/testing';
import { GradeBadge } from './grade-badge';

describe('GradeBadge', () => {
  function render(grade: 'A' | 'B' | 'C' | 'D' | 'F' | null) {
    const fixture = TestBed.createComponent(GradeBadge);
    fixture.componentRef.setInput('grade', grade);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the letter for a grade', () => {
    expect(render('B').textContent?.trim()).toBe('B');
  });

  it('falls back to em-dash for null grade', () => {
    expect(render(null).textContent?.trim()).toBe('—');
  });

  it('applies a color class per grade', () => {
    expect(render('A').querySelector('.badge-a')).toBeTruthy();
    expect(render('F').querySelector('.badge-f')).toBeTruthy();
    expect(render(null).querySelector('.badge-unknown')).toBeTruthy();
  });
});
