import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SeoService } from '../../seo/seo.service';
import { aboutMeta } from '../../seo/route-meta';

@Component({
  selector: 'app-about',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './about.html',
  styleUrl: './about.scss',
})
export class About {
  constructor() {
    inject(SeoService).setMeta(aboutMeta());
  }
}
