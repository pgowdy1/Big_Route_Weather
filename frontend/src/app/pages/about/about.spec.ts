import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { About } from './about';

describe('About', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [About],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('renders the page <h1> title and the "Why I made this" section heading', () => {
    const fixture = TestBed.createComponent(About);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('About Big Route Weather');
    expect(compiled.querySelector('h2')?.textContent).toContain('Why I made this');
  });

  it('names NWS and SNOTEL as data sources', () => {
    const fixture = TestBed.createComponent(About);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('NWS');
    expect(text).toContain('SNOTEL');
  });

  it('links the contact email', () => {
    const fixture = TestBed.createComponent(About);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    const link = compiled.querySelector<HTMLAnchorElement>('a.email');
    expect(link?.getAttribute('href')).toBe('mailto:pgowdy1@gmail.com');
  });
});
