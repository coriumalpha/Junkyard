import { Component, input, output, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DescriptionViewComponent } from './description-view.component';
@Component({
 selector:'app-description-editor',standalone:true,imports:[FormsModule,DescriptionViewComponent,DecimalPipe],
 template:`<section class="description-editor"><header><div><h3>Descripción</h3><p>Qué es, para qué sirve y qué conviene recordar.</p></div><label><input type="checkbox" [checked]="markdown()" (change)="markdownChange.emit($any($event.target).checked)" /> Markdown</label></header>
 <nav aria-label="Modo del editor"><button type="button" [attr.aria-pressed]="!preview()" (click)="preview.set(false)">Escribir</button><button type="button" [attr.aria-pressed]="preview()" (click)="preview.set(true)">Vista previa</button><span>{{ value().length | number }} caracteres</span></nav>
 @if (preview()) { <div class="preview"><app-description-view [text]="value() || 'Todavía no hay descripción.'" [markdown]="markdown()" /></div> }
 @else { <textarea aria-label="Descripción del ítem" [ngModel]="value()" (ngModelChange)="valueChange.emit($event)" rows="10" placeholder="Documenta el objeto: uso, conexiones, especificaciones, historia, pruebas…"></textarea> }
 @if (markdown()) { <details><summary>Ayuda de formato</summary><p><code>## Sección</code> · <code>**negrita**</code> · <code>*cursiva*</code> · <code>- lista</code> · <code>[enlace](https://…)</code>. También tablas, citas y bloques de código. HTML e imágenes remotas no se cargan.</p></details> }
 </section>`,styleUrl:'./description-editor.component.scss'
})
export class DescriptionEditorComponent {
 readonly value=input('');readonly markdown=input(true);
 readonly valueChange=output<string>();readonly markdownChange=output<boolean>();
 protected readonly preview=signal(false);
}
