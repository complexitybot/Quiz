import React from "react";
import '../../styles/EditorForm.css'

export class CreateLevelForm extends React.Component {
    constructor(props) {
        super(props);
        this.state = {
            topic: 'Сложность алгоритмов',
            level: ''
        };

        this.handleInputChange = this.handleInputChange.bind(this);
        this.handleSubmit = this.handleSubmit.bind(this);
    }

    handleInputChange(event) {
        const target = event.target;
        const name = target.name;
        const value = event.target.value;
        this.setState({
            [name]: value
        });
    }

    handleSubmit(event) {
        alert('Был создан пустой Level: ' + this.state.kevek);
        event.preventDefault();
    }

    render() {
        return (
            <form onSubmit={this.handleSubmit}>
                <h3>Добавление Level</h3>
                <label>
                    <p>Выберите Topic, в который хотите добавить Level:</p>
                    <select name="topic" value={this.state.topic} onChange={this.handleInputChange}>
                        <option value="Сложность алгоритмов">Сложность алгоритмов</option>
                    </select>
                </label>
                <br/>
                <label>
                    <p>Имя Level, который хотите добавить:</p>
                    <textarea className="smallTextarea" name="level" value={this.state.level} onChange={this.handleInputChange} />
                </label>
                <br/><br/>
                <input className="button2" type="submit" value="Create Level" />
            </form>
        );
    }
}